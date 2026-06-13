using System.Text;
using System.Text.Json;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Worker.Abstractions;
using laundryghar.Worker.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace laundryghar.Worker.Infrastructure.Channels;

/// <summary>
/// Sends push notifications via the Expo Push API.
/// POST https://exp.host/--/api/v2/push/send
///
/// Token resolution: looks up active tokens in <c>engagement_cms.push_tokens</c>
/// for the outbox row's recipient. Gracefully no-ops when no token is found.
///
/// Only enabled when <c>Notifications:Push:Enabled = true</c>.
/// Otherwise the caller falls back to <see cref="LoggingChannelSender"/>.
/// </summary>
internal sealed class ExpoPushChannelSender : IChannelSender
{
    private const string ProviderName  = "expo";
    private const string ExpoApiUrl    = "https://exp.host/--/api/v2/push/send";

    private readonly IHttpClientFactory _httpFactory;
    private readonly PushOptions        _options;
    private readonly LaundryGharDbContext _db;
    private readonly ILogger<ExpoPushChannelSender> _logger;

    public ExpoPushChannelSender(
        IHttpClientFactory httpFactory,
        IOptions<PushOptions> options,
        LaundryGharDbContext db,
        ILogger<ExpoPushChannelSender> logger)
    {
        _httpFactory = httpFactory;
        _options     = options.Value;
        _db          = db;
        _logger      = logger;
    }

    public async Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken ct = default)
    {
        // Resolve active tokens for this recipient.
        var tokens = await ResolveTokensAsync(request, ct);

        if (tokens.Count == 0)
        {
            _logger.LogDebug(
                "[Push/Expo] No active tokens for recipient={RecipientType}:{RecipientId} " +
                "outboxId={OutboxId} — skipping (no-op).",
                request.RecipientType, request.RecipientId, request.OutboxId);
            // Graceful no-op — mark as sent so the dispatcher doesn't retry endlessly.
            return new ChannelSendResult(ProviderName);
        }

        // Resolve deep-link type/id from the notification's reference.
        // mapping: order → "order", pickup_request → "pickup",
        //          delivery_assignment / assignment → "task"; omit when reference is null.
        var deepLinkType = MapReferenceTypeToDeepLink(request.ReferenceType);

        // Build one message per token (Expo supports batches but we keep it simple).
        var messages = tokens.Select(token => new
        {
            to      = token,
            title   = (string?)null,   // body only; title set from PushTitle if present
            body    = request.Body,
            data    = BuildData(request.OutboxId, request.TemplateCode, deepLinkType, request.ReferenceId),
            sound   = "default",
            // channelId required for Android >= 8 (Expo creates 'default' automatically).
            channelId = "default"
        }).ToList();

        var json    = JsonSerializer.Serialize(messages);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var client = _httpFactory.CreateClient("push");

        _logger.LogDebug(
            "[Push/Expo] Sending to {Count} token(s) for recipient={RecipientType}:{RecipientId} " +
            "outboxId={OutboxId}",
            tokens.Count, request.RecipientType, request.RecipientId, request.OutboxId);

        var response = await client.PostAsync(ExpoApiUrl, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Expo Push API returned {(int)response.StatusCode}: {errorBody}");
        }

        _logger.LogInformation(
            "[Push/Expo] Sent to {Count} token(s) outboxId={OutboxId}",
            tokens.Count, request.OutboxId);

        return new ChannelSendResult(ProviderName);
    }

    /// <summary>
    /// Maps a notifications_outbox.reference_type value to the deep-link type string
    /// understood by the mobile app. Returns <c>null</c> when the reference type is
    /// unknown or absent so the field is omitted from the push payload.
    /// </summary>
    internal static string? MapReferenceTypeToDeepLink(string? referenceType)
        => referenceType?.ToLowerInvariant() switch
        {
            "order"                   => "order",
            "pickup_request"          => "pickup",
            "delivery_assignment"     => "task",
            "assignment"              => "task",
            _                         => null
        };

    /// <summary>
    /// Builds the Expo push <c>data</c> object. Always includes <c>outboxId</c> and
    /// <c>templateCode</c>. Appends <c>type</c>/<c>id</c> when a deep-link is resolvable.
    /// Returns an anonymous object; serialised by <see cref="System.Text.Json.JsonSerializer"/>.
    /// </summary>
    private static object BuildData(
        Guid    outboxId,
        string? templateCode,
        string? deepLinkType,
        Guid?   referenceId)
    {
        if (deepLinkType is not null && referenceId.HasValue)
        {
            return new
            {
                outboxId,
                templateCode,
                type = deepLinkType,
                id   = referenceId.Value
            };
        }

        return new { outboxId, templateCode };
    }

    private async Task<List<string>> ResolveTokensAsync(
        ChannelSendRequest request,
        CancellationToken ct)
    {
        if (!request.RecipientId.HasValue)
            return [];

        var query = _db.PushTokens
            .Where(pt => pt.BrandId == request.BrandId && pt.IsActive);

        if (request.RecipientType.Equals("customer", StringComparison.OrdinalIgnoreCase))
            query = query.Where(pt => pt.CustomerId == request.RecipientId.Value);
        else
            query = query.Where(pt => pt.UserId == request.RecipientId.Value);

        return await query
            .Select(pt => pt.Token)
            .ToListAsync(ct);
    }
}
