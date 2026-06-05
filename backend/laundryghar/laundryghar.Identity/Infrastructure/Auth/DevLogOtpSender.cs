namespace laundryghar.Identity.Infrastructure.Auth;

/// <summary>
/// Development OTP sender — logs the plain-text code via ILogger.
/// Replace with MSG91 / Twilio impl in production by swapping this registration.
/// </summary>
public sealed class DevLogOtpSender : IOtpSender
{
    private readonly ILogger<DevLogOtpSender> _logger;

    public DevLogOtpSender(ILogger<DevLogOtpSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string identifier, string identifierType, string plainCode, string purpose, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "[DEV-OTP] identifier={Identifier} type={IdentifierType} purpose={Purpose} code={Code}",
            identifier, identifierType, purpose, plainCode);

        return Task.CompletedTask;
    }
}
