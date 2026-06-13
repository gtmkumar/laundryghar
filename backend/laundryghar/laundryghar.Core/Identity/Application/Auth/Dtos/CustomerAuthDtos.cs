namespace laundryghar.Identity.Application.Auth.Dtos;

// ─── Requests ──────────────────────────────────────────────────────────────

/// <param name="Phone">E.164 phone number, e.g. +919876543210</param>
/// <param name="BrandCode">Optional; resolved from X-Brand-Id header if omitted.</param>
public sealed record CustomerOtpSendRequest(string Phone, string? BrandCode = null);

public sealed record CustomerOtpVerifyRequest(string Phone, string Code, string? BrandCode = null);

// ─── Responses ─────────────────────────────────────────────────────────────

public sealed record CustomerTokenResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    string TokenType = "Bearer",
    bool IsNewCustomer = false
);

public sealed record CustomerMeResponse(
    Guid CustomerId,
    Guid BrandId,
    string Phone,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string Status
);
