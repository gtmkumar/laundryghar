namespace laundryghar.Utilities.Exceptions;

/// <summary>
/// Thrown when a request needs to name the brand it acts on and none could be resolved — no
/// <c>brand_id</c> claim on the token and no <c>X-Brand-Id</c> header. Maps to HTTP 400 carrying
/// the stable code <see cref="ErrorCode"/>.
/// </summary>
/// <remarks>
/// This is deliberately <b>not</b> an authentication failure. The token is valid and the caller is
/// who they say they are; what is missing is a request parameter. It used to throw
/// <see cref="UnauthorizedAccessException"/>, which the exception handler maps to 401 — and 401 is
/// the one status clients treat as "this session is dead". <c>admin-web</c>'s interceptor
/// (<c>src/api/client.ts</c>) responds to it by refreshing the token and, when that changes
/// nothing, clearing auth and bouncing to <c>/login</c>. So a platform admin who simply had not
/// picked a brand yet was logged out, and because the envelope flattens the message to
/// "Unauthorized." nobody — operator or developer — could see why.
///
/// Found by the live audit: 60 of the platform admin's 141 endpoint calls returned 401 for this
/// reason alone, and every one of them turned into a 200 once the header was supplied. See
/// <c>docs/ABAC_AUDIT_2026-08-31.md</c> finding F-4.
///
/// The code rides in the <c>errorMessage</c> dictionary under <c>"code"</c>, matching the shape
/// <see cref="StructuredBusinessRuleException"/> and the authorization handler's
/// <c>step_up_required</c> / <c>feature_not_in_plan</c> already use, so a client can branch on it
/// and prompt for a brand instead of guessing from prose.
/// </remarks>
public sealed class BrandContextRequiredException : AppExceptionBase
{
    /// <summary>Stable machine-readable identifier surfaced to clients.</summary>
    public const string ErrorCode = "brand_context_required";

    private const string DefaultTitle = "Brand Context Required";

    public const string DefaultMessage =
        "Brand context required. Pass the X-Brand-Id header to name the brand this request acts on.";

    public BrandContextRequiredException()
        : base(DefaultTitle, DefaultMessage) { }

    public BrandContextRequiredException(string message)
        : base(DefaultTitle, message) { }
}
