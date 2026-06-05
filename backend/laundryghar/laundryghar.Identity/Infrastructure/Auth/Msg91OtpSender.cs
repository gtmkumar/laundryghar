namespace laundryghar.Identity.Infrastructure.Auth;

/// <summary>
/// Production stub for the MSG91 OTP sender.
/// Registered in non-Development environments so the app can't silently log OTP codes.
/// Replace the body with the real MSG91 HTTP integration before going to production.
/// </summary>
public sealed class Msg91OtpSender : IOtpSender
{
    public Task SendAsync(
        string identifier, string identifierType,
        string plainCode, string purpose,
        CancellationToken ct = default)
    {
        // TODO: integrate MSG91 / Twilio here before going live.
        throw new NotImplementedException(
            "MSG91 OTP sender is not yet implemented. " +
            "Register the real implementation before deploying to production.");
    }
}
