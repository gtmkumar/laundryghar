using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using laundryghar.Worker.Abstractions;
using laundryghar.Worker.Options;
using Microsoft.Extensions.Options;

namespace laundryghar.Worker.Infrastructure.Channels;

/// <summary>
/// Sends WhatsApp template messages via the Meta WhatsApp Cloud API v21.0.
/// POST https://graph.facebook.com/v21.0/{phoneNumberId}/messages
///
/// Uses the template-message structure: type=template + template name + language + body parameters.
/// The template name is taken from the outbox row's TemplateCode; the body is passed as a
/// text body parameter so the rendered body is reflected in the message.
///
/// Only enabled when <c>Notifications:WhatsApp:Enabled = true</c> AND AccessToken + PhoneNumberId
/// are non-empty. Otherwise the caller falls back to <see cref="LoggingChannelSender"/>.
/// </summary>
internal sealed class WhatsAppCloudChannelSender : IChannelSender
{
    private const string ProviderName = "meta-whatsapp-cloud";
    private const string GraphApiBase = "https://graph.facebook.com/v21.0";

    private readonly IHttpClientFactory _httpFactory;
    private readonly WhatsAppOptions    _options;
    private readonly ILogger<WhatsAppCloudChannelSender> _logger;

    public WhatsAppCloudChannelSender(
        IHttpClientFactory httpFactory,
        IOptions<WhatsAppOptions> options,
        ILogger<WhatsAppCloudChannelSender> logger)
    {
        _httpFactory = httpFactory;
        _options     = options.Value;
        _logger      = logger;
    }

    public async Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken ct = default)
    {
        var phone = request.RecipientPhone
            ?? throw new InvalidOperationException(
                $"WhatsApp send requires RecipientPhone. OutboxId={request.OutboxId}");

        // Normalise: Meta requires E.164 without leading '+' for the 'to' field.
        var toNumber = phone.TrimStart('+');

        // Build the template message payload.
        // Template name = template code in lower-snake (Meta convention).
        var templateName = (request.TemplateCode ?? "order_notification")
            .ToLowerInvariant()
            .Replace('_', '_'); // already snake_case

        var payload = new
        {
            messaging_product = "whatsapp",
            to   = toNumber,
            type = "template",
            template = new
            {
                name     = templateName,
                language = new { code = "en" },
                components = new[]
                {
                    new
                    {
                        type       = "body",
                        parameters = new[]
                        {
                            new { type = "text", text = request.Body }
                        }
                    }
                }
            }
        };

        var json    = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var client  = _httpFactory.CreateClient("whatsapp");
        var url = $"{GraphApiBase}/{_options.PhoneNumberId}/messages";

        _logger.LogDebug(
            "[WhatsApp] Sending template={Template} to={To} outboxId={OutboxId}",
            templateName, MaskPhone(toNumber), request.OutboxId);

        var response = await client.PostAsync(url, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"WhatsApp Cloud API returned {(int)response.StatusCode}: {errorBody}");
        }

        // Parse provider message id from response.
        string? msgId = null;
        try
        {
            var respJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(respJson);
            if (doc.RootElement.TryGetProperty("messages", out var msgs)
                && msgs.GetArrayLength() > 0)
                msgId = msgs[0].GetProperty("id").GetString();
        }
        catch { /* best-effort — don't fail delivery because parsing failed */ }

        _logger.LogInformation(
            "[WhatsApp] Sent outboxId={OutboxId} template={Template} to={To} msgId={MsgId}",
            request.OutboxId, templateName, MaskPhone(toNumber), msgId);

        return new ChannelSendResult(ProviderName, msgId);
    }

    private static string MaskPhone(string phone)
        => phone.Length > 4 ? $"****{phone[^4..]}" : "****";
}
