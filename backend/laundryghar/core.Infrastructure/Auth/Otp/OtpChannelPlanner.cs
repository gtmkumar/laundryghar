using laundryghar.SharedDataModel.Common;

namespace core.Infrastructure.Auth;

/// <summary>Delivery channels an OTP can be dispatched over, in priority order.</summary>
public enum OtpChannel
{
    WhatsApp,
    Sms,
    DevLog,
}

/// <summary>
/// Pure channel-selection logic for OTP delivery, kept free of I/O so the
/// routing matrix is unit-testable. <see cref="RoutingOtpSender"/> executes
/// the plan in order, falling through to the next channel on failure.
/// </summary>
public static class OtpChannelPlanner
{
    /// <summary>
    /// Builds the ordered channel plan for one OTP dispatch.
    ///
    /// Rules:
    ///  • WhatsApp and SMS apply only to phone identifiers.
    ///  • WhatsApp requires the integration to be Enabled (admin toggle) AND
    ///    OtpEnabled AND PhoneNumberId + AccessToken + OtpTemplateName.
    ///  • SMS (MSG91) requires Enabled AND AuthKey + DltTemplateId.
    ///  • Development always appends DevLog as the last resort so a fresh
    ///    checkout with no integrations configured keeps working.
    /// An empty plan means no channel is configured (caller decides to throw).
    /// </summary>
    public static IReadOnlyList<OtpChannel> Plan(
        WhatsAppSettings whatsApp,
        SmsSettings sms,
        string identifierType,
        bool isDevelopment)
    {
        var plan = new List<OtpChannel>(3);
        var isPhone = string.Equals(identifierType, "phone", StringComparison.OrdinalIgnoreCase);

        if (isPhone && IsWhatsAppOtpConfigured(whatsApp))
            plan.Add(OtpChannel.WhatsApp);

        if (isPhone && IsSmsConfigured(sms))
            plan.Add(OtpChannel.Sms);

        if (isDevelopment)
            plan.Add(OtpChannel.DevLog);

        return plan;
    }

    public static bool IsWhatsAppOtpConfigured(WhatsAppSettings s) =>
        s.Enabled
        && s.OtpEnabled
        && !string.IsNullOrWhiteSpace(s.PhoneNumberId)
        && !string.IsNullOrWhiteSpace(s.AccessToken)
        && !string.IsNullOrWhiteSpace(s.OtpTemplateName);

    public static bool IsSmsConfigured(SmsSettings s) =>
        s.Enabled
        && !string.IsNullOrWhiteSpace(s.AuthKey)
        && !string.IsNullOrWhiteSpace(s.DltTemplateId);
}
