namespace laundryghar.Utilities.ApiResponse.ResponseUtil;

/// <summary>
/// Error type codes aligned with standard HTTP status codes (RFC 9110).
/// Clients can map these values directly to HTTP responses.
/// </summary>
public enum ErrorMessageEnum
{
    BadRequest = 400,
    UnAuthorized = 401,
    // 402 is the plan/billing wall (PLATFORM_STRATEGY.md §5 feature_not_in_plan, §9 suspension).
    // It must exist here because this enum is what clients read out of the body: emitting a 402 on
    // the wire with Forbidden(403) inside contradicts the one distinction those responses carry —
    // "your plan lacks this" vs "your role lacks this".
    PaymentRequired = 402,
    Forbidden = 403,
    NotFound = 404,
    Conflict = 409,
    ValidationFailed = 422,
    UpgradeRequired = 426,
    Error = 500
}
