using System.Text.Json;
using laundryghar.SharedDataModel.Common;

namespace laundryghar.Identity.Infrastructure.Auth;

/// <summary>
/// Delivers an OTP via the MSG91 v5 send-OTP API:
/// POST https://control.msg91.com/api/v5/otp?template_id=…&amp;mobile=…&amp;otp=…
/// with the account auth key in the 'authkey' header (never in the URL).
///
/// We pass our own pre-generated code via the 'otp' query parameter so MSG91
/// only transports it — generation, hashing, and verification stay in
/// OtpSendHandler/OtpVerifyHandler exactly as for every other channel.
/// </summary>
public sealed class Msg91OtpDispatcher
{
    private const string OtpApiBase = "https://control.msg91.com/api/v5/otp";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<Msg91OtpDispatcher> _logger;

    public Msg91OtpDispatcher(IHttpClientFactory httpFactory, ILogger<Msg91OtpDispatcher> logger)
    {
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    /// <summary>
    /// Builds the request URI. Public + pure for unit tests.
    /// MSG91 expects the mobile as country code + number with no '+'
    /// (e.g. 919876543210).
    /// </summary>
    public static Uri BuildRequestUri(string phoneE164, string templateId, string code)
    {
        var mobile = phoneE164.TrimStart('+');
        return new Uri(
            $"{OtpApiBase}?template_id={Uri.EscapeDataString(templateId)}" +
            $"&mobile={Uri.EscapeDataString(mobile)}" +
            $"&otp={Uri.EscapeDataString(code)}");
    }

    public async Task SendAsync(SmsSettings settings, string phoneE164, string code, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("msg91-otp");
        using var request = new HttpRequestMessage(
            HttpMethod.Post, BuildRequestUri(phoneE164, settings.DltTemplateId!, code));
        request.Headers.Add("authkey", settings.AuthKey);

        var response = await client.SendAsync(request, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        // MSG91 returns 200 with {"type":"success"|"error", "message": …} —
        // a transport 200 is NOT delivery acceptance, so check the type field.
        if (!response.IsSuccessStatusCode || !IsSuccessBody(body))
            throw new HttpRequestException(
                $"MSG91 OTP API returned {(int)response.StatusCode}: {body}");

        _logger.LogInformation("[OTP] SMS (MSG91) delivered to={To}", WhatsAppOtpDispatcher.MaskPhone(phoneE164));
    }

    /// <summary>Public + pure for unit tests.</summary>
    public static bool IsSuccessBody(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("type", out var t)
                && string.Equals(t.GetString(), "success", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
