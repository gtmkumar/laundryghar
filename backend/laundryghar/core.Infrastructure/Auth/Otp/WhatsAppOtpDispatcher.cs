using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using laundryghar.SharedDataModel.Common;
using Microsoft.Extensions.Logging;

namespace core.Infrastructure.Auth;

/// <summary>
/// Delivers an OTP via a Meta WhatsApp Cloud API authentication template.
/// POST https://graph.facebook.com/v21.0/{phoneNumberId}/messages
///
/// Authentication-category templates require the code as BOTH a body parameter
/// and a url-button parameter (the copy-code button) — Meta rejects the send
/// otherwise. Mirrors the Worker's WhatsAppCloudChannelSender HTTP shape.
/// </summary>
public sealed class WhatsAppOtpDispatcher
{
    private const string GraphApiBase = "https://graph.facebook.com/v21.0";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<WhatsAppOtpDispatcher> _logger;

    public WhatsAppOtpDispatcher(IHttpClientFactory httpFactory, ILogger<WhatsAppOtpDispatcher> logger)
    {
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    /// <summary>
    /// Builds the authentication-template payload. Public + pure for unit tests.
    /// Meta requires the 'to' number in E.164 WITHOUT the leading '+'.
    /// </summary>
    public static object BuildPayload(string phoneE164, string templateName, string code) => new
    {
        messaging_product = "whatsapp",
        to   = phoneE164.TrimStart('+'),
        type = "template",
        template = new
        {
            name     = templateName,
            language = new { code = "en" },
            components = new object[]
            {
                new
                {
                    type       = "body",
                    parameters = new[] { new { type = "text", text = code } }
                },
                new
                {
                    type       = "button",
                    sub_type   = "url",
                    index      = "0",
                    parameters = new[] { new { type = "text", text = code } }
                },
            }
        }
    };

    public async Task SendAsync(WhatsAppSettings settings, string phoneE164, string code, CancellationToken ct)
    {
        var payload = BuildPayload(phoneE164, settings.OtpTemplateName!, code);
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var client = _httpFactory.CreateClient("whatsapp-otp");
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{GraphApiBase}/{settings.PhoneNumberId}/messages")
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"WhatsApp Cloud API returned {(int)response.StatusCode}: {errorBody}");
        }

        _logger.LogInformation(
            "[OTP] WhatsApp delivered template={Template} to={To}",
            settings.OtpTemplateName, MaskPhone(phoneE164));
    }

    public static string MaskPhone(string phone)
        => phone.Length > 4 ? $"****{phone[^4..]}" : "****";
}
