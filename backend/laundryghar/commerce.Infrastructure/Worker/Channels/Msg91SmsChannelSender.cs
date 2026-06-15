using System.Text;
using System.Text.Json;
using commerce.Infrastructure.Worker.Abstractions;
using commerce.Infrastructure.Worker.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace commerce.Infrastructure.Worker.Channels;

/// <summary>
/// Sends transactional SMS via the MSG91 API (India DLT-compliant route).
///
/// Endpoint: POST https://control.msg91.com/api/v5/flow/
/// Auth:     authkey header (plain — MSG91 does not use Bearer).
/// Body:     flow_id (DLT template registered with MSG91), recipients list.
///
/// India DLT (Distributed Ledger Technology) rules require:
///   • A registered sender ID (6-char alpha for transactional).
///   • A TRAI-approved template ID (DltTemplateId config key).
///   • Messages must match the approved template body exactly; the rendered body
///     text is sent as-is (MSG91 uses flow variables separately, but we send the
///     pre-rendered body via the 'VAR1' variable mapped to the full message body).
///
/// Only enabled when <c>Notifications:Sms:Enabled = true</c> AND AuthKey + SenderId
/// are non-empty. Otherwise the caller falls back to
/// <see cref="commerce.Infrastructure.Worker.Stubs.LoggingChannelSender"/>.
/// </summary>
public sealed class Msg91SmsChannelSender : IChannelSender
{
    private const string ProviderName = "msg91";
    private const string Msg91FlowUrl = "https://control.msg91.com/api/v5/flow/";

    private readonly IHttpClientFactory _httpFactory;
    private readonly SmsOptions         _options;
    private readonly ILogger<Msg91SmsChannelSender> _logger;

    public Msg91SmsChannelSender(
        IHttpClientFactory httpFactory,
        IOptions<SmsOptions> options,
        ILogger<Msg91SmsChannelSender> logger)
    {
        _httpFactory = httpFactory;
        _options     = options.Value;
        _logger      = logger;
    }

    public async Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken ct = default)
    {
        var phone = request.RecipientPhone
            ?? throw new InvalidOperationException(
                $"SMS send requires RecipientPhone. OutboxId={request.OutboxId}");

        // MSG91 expects mobile number without country code prefix for Indian numbers
        // but accepts E.164 (+91...). We strip leading '+' for safety.
        var mobile = phone.TrimStart('+');

        // MSG91 flow API — DLT template is mapped via flow_id.
        // We pass the pre-rendered body as a variable so the DLT-approved template
        // text is preserved. Operators receive the approved body, not a free-form message.
        var payload = new
        {
            flow_id    = _options.DltTemplateId,
            sender     = _options.SenderId,
            mobiles    = mobile,
            // VAR1 maps to the first variable slot in the registered flow template.
            // The admin must configure their MSG91 flow to include {{VAR1}} at the
            // appropriate position in the approved DLT template.
            VAR1       = request.Body
        };

        var json    = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var client = _httpFactory.CreateClient("sms");

        _logger.LogDebug(
            "[SMS/MSG91] Sending template={Template} to={To} outboxId={OutboxId}",
            request.TemplateCode, MaskPhone(mobile), request.OutboxId);

        var response = await client.PostAsync(Msg91FlowUrl, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"MSG91 returned {(int)response.StatusCode}: {errorBody}");
        }

        // MSG91 returns 200 even on logical errors; check response body.
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        if (responseBody.Contains("\"type\":\"error\"", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"MSG91 logical error: {responseBody}");
        }

        _logger.LogInformation(
            "[SMS/MSG91] Sent outboxId={OutboxId} template={Template} to={To}",
            request.OutboxId, request.TemplateCode, MaskPhone(mobile));

        return new ChannelSendResult(ProviderName);
    }

    private static string MaskPhone(string phone)
        => phone.Length > 4 ? $"****{phone[^4..]}" : "****";
}
