namespace laundryghar.Identity.Application.Auth.Dtos;

// ─── Request DTOs ──────────────────────────────────────────────────────────

public sealed record PasswordLoginRequest(string Identifier, string Password);

public sealed record OtpSendRequest(string Identifier, string IdentifierType, string Purpose);

public sealed record OtpVerifyRequest(string Identifier, string IdentifierType, string Purpose, string Code);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record ForgotPasswordRequest(string Identifier, string IdentifierType);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

// ─── Response DTOs ─────────────────────────────────────────────────────────

public sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresInSeconds, string TokenType = "Bearer");

public sealed record OtpSentResponse(string Message, DateTimeOffset ExpiresAt);

public sealed record OtpVerifiedResponse(string AccessToken, string RefreshToken, int ExpiresInSeconds, string TokenType = "Bearer");
