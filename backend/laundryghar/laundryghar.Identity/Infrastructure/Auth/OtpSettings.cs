namespace laundryghar.Identity.Infrastructure.Auth;

public sealed class OtpSettings
{
    public const string SectionName = "Otp";

    public int TtlMinutes  { get; set; } = 5;
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Minimum seconds that must elapse before a new OTP can be issued for the same identifier+purpose.</summary>
    public int ResendCooldownSeconds { get; set; } = 60;
}
