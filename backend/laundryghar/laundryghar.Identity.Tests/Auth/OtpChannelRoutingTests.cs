using System.Text.Json;
using laundryghar.Identity.Infrastructure.Auth;
using laundryghar.SharedDataModel.Common;

namespace laundryghar.Identity.Tests.Auth;

/// <summary>
/// Unit tests for the OTP channel routing introduced with WhatsApp OTP delivery:
///   - OtpChannelPlanner.Plan: WhatsApp-first ordering, SMS fallback, dev-log
///     last resort, phone-only gating, per-channel config requirements
///   - WhatsAppOtpDispatcher.BuildPayload: auth-template shape (body + copy-code
///     button both carry the code), '+'-stripped recipient
///   - Msg91OtpDispatcher.BuildRequestUri / IsSuccessBody
/// </summary>
public sealed class OtpChannelRoutingTests
{
    private static WhatsAppSettings WaConfigured() => new()
    {
        Enabled         = true,
        OtpEnabled      = true,
        PhoneNumberId   = "123456789012345",
        AccessToken     = "EAAB-token",
        OtpTemplateName = "otp_login",
    };

    private static SmsSettings SmsConfigured() => new()
    {
        Enabled       = true,
        AuthKey       = "msg91-key",
        DltTemplateId = "tmpl-123",
    };

    // ── Plan: ordering ───────────────────────────────────────────────────────

    [Fact]
    public void Plan_WhatsAppBeforeSms_WhenBothConfigured()
    {
        var plan = OtpChannelPlanner.Plan(WaConfigured(), SmsConfigured(), "phone", isDevelopment: false);
        Assert.Equal([OtpChannel.WhatsApp, OtpChannel.Sms], plan);
    }

    [Fact]
    public void Plan_SmsOnly_WhenWhatsAppOtpDisabled()
    {
        var wa = WaConfigured();
        wa.OtpEnabled = false;
        var plan = OtpChannelPlanner.Plan(wa, SmsConfigured(), "phone", isDevelopment: false);
        Assert.Equal([OtpChannel.Sms], plan);
    }

    [Fact]
    public void Plan_WhatsAppRequiresMasterToggle_NotJustOtpToggle()
    {
        // Admin can flip OtpEnabled on while the integration master toggle is off —
        // credentials are only considered valid alongside Enabled.
        var wa = WaConfigured();
        wa.Enabled = false;
        var plan = OtpChannelPlanner.Plan(wa, SmsConfigured(), "phone", isDevelopment: false);
        Assert.Equal([OtpChannel.Sms], plan);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Plan_WhatsAppSkipped_WhenTemplateNameMissing(string? template)
    {
        var wa = WaConfigured();
        wa.OtpTemplateName = template;
        var plan = OtpChannelPlanner.Plan(wa, SmsConfigured(), "phone", isDevelopment: false);
        Assert.Equal([OtpChannel.Sms], plan);
    }

    [Fact]
    public void Plan_SmsSkipped_WhenDltTemplateMissing()
    {
        var sms = SmsConfigured();
        sms.DltTemplateId = null;
        var plan = OtpChannelPlanner.Plan(WaConfigured(), sms, "phone", isDevelopment: false);
        Assert.Equal([OtpChannel.WhatsApp], plan);
    }

    [Fact]
    public void Plan_Empty_WhenNothingConfigured_OutsideDevelopment()
    {
        var plan = OtpChannelPlanner.Plan(new WhatsAppSettings(), new SmsSettings(), "phone", isDevelopment: false);
        Assert.Empty(plan);
    }

    // ── Plan: dev-log last resort ────────────────────────────────────────────

    [Fact]
    public void Plan_DevLogAppendedLast_InDevelopment()
    {
        var plan = OtpChannelPlanner.Plan(WaConfigured(), SmsConfigured(), "phone", isDevelopment: true);
        Assert.Equal([OtpChannel.WhatsApp, OtpChannel.Sms, OtpChannel.DevLog], plan);
    }

    [Fact]
    public void Plan_DevLogOnly_WhenNothingConfigured_InDevelopment()
    {
        var plan = OtpChannelPlanner.Plan(new WhatsAppSettings(), new SmsSettings(), "phone", isDevelopment: true);
        Assert.Equal([OtpChannel.DevLog], plan);
    }

    // ── Plan: identifier-type gating ─────────────────────────────────────────

    [Fact]
    public void Plan_EmailIdentifier_NeverRoutesToPhoneChannels()
    {
        var plan = OtpChannelPlanner.Plan(WaConfigured(), SmsConfigured(), "email", isDevelopment: false);
        Assert.Empty(plan);
    }

    [Fact]
    public void Plan_PhoneIdentifierType_IsCaseInsensitive()
    {
        var plan = OtpChannelPlanner.Plan(WaConfigured(), SmsConfigured(), "Phone", isDevelopment: false);
        Assert.Contains(OtpChannel.WhatsApp, plan);
    }

    // ── WhatsApp payload ─────────────────────────────────────────────────────

    [Fact]
    public void BuildPayload_AuthTemplate_CarriesCodeInBodyAndCopyButton()
    {
        var payload = WhatsAppOtpDispatcher.BuildPayload("+919800000001", "otp_login", "419766");
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = doc.RootElement;

        Assert.Equal("whatsapp", root.GetProperty("messaging_product").GetString());
        Assert.Equal("919800000001", root.GetProperty("to").GetString()); // '+' stripped
        Assert.Equal("template", root.GetProperty("type").GetString());

        var template = root.GetProperty("template");
        Assert.Equal("otp_login", template.GetProperty("name").GetString());

        var components = template.GetProperty("components");
        Assert.Equal(2, components.GetArrayLength());

        var body = components[0];
        Assert.Equal("body", body.GetProperty("type").GetString());
        Assert.Equal("419766", body.GetProperty("parameters")[0].GetProperty("text").GetString());

        // Authentication templates REQUIRE the copy-code url button parameter.
        var button = components[1];
        Assert.Equal("button", button.GetProperty("type").GetString());
        Assert.Equal("url", button.GetProperty("sub_type").GetString());
        Assert.Equal("0", button.GetProperty("index").GetString());
        Assert.Equal("419766", button.GetProperty("parameters")[0].GetProperty("text").GetString());
    }

    // ── MSG91 request ────────────────────────────────────────────────────────

    [Fact]
    public void BuildRequestUri_StripsPlus_AndCarriesTemplateAndCode()
    {
        var uri = Msg91OtpDispatcher.BuildRequestUri("+919800000001", "tmpl-123", "419766");
        Assert.StartsWith("https://control.msg91.com/api/v5/otp?", uri.AbsoluteUri);
        Assert.Contains("template_id=tmpl-123", uri.Query);
        Assert.Contains("mobile=919800000001", uri.Query);
        Assert.Contains("otp=419766", uri.Query);
        // The account auth key must travel in a header, never the URL.
        Assert.DoesNotContain("authkey", uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""{"type":"success","request_id":"x"}""", true)]
    [InlineData("""{"type":"SUCCESS"}""", true)]
    [InlineData("""{"type":"error","message":"Invalid template"}""", false)]
    [InlineData("""{"message":"no type field"}""", false)]
    [InlineData("not-json", false)]
    public void IsSuccessBody_ChecksMsg91TypeField(string body, bool expected)
    {
        Assert.Equal(expected, Msg91OtpDispatcher.IsSuccessBody(body));
    }

    // ── Phone masking ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("+919800000001", "****0001")]
    [InlineData("123", "****")]
    public void MaskPhone_ShowsAtMostLastFour(string phone, string expected)
    {
        Assert.Equal(expected, WhatsAppOtpDispatcher.MaskPhone(phone));
    }
}
