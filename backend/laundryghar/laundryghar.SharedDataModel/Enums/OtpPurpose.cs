namespace laundryghar.SharedDataModel.Enums;

public static class OtpPurpose
{
    public const string Login = "login";
    public const string Signup = "signup";
    public const string VerifyPhone = "verify_phone";
    public const string VerifyEmail = "verify_email";
    public const string ResetPassword = "reset_password";
    public const string Transaction = "transaction";
    public const string DeliveryOtp = "delivery_otp";
    public const string SensitiveAction = "sensitive_action";

    /// <summary>RaaS partner OTP login (issue #14). Keeps partner login OTPs in their own
    /// namespace so lockout/verify windows never collide with customer/staff login OTPs for
    /// the same phone.</summary>
    public const string PartnerLogin = "partner_login";
}
