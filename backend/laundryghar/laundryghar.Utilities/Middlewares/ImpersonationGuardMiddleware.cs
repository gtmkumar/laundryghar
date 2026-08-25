using System.Text.Json;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.Utilities.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace laundryghar.Utilities.Middlewares;

/// <summary>Keys shared between the guard and the audit stamper.</summary>
public static class ImpersonationKeys
{
    /// <summary><see cref="HttpContext.Items"/> key holding the VALIDATED grant id for this request.
    /// Set only after the guard has confirmed the grant against the database.</summary>
    public const string GrantIdItem = "impersonation.grant_id";

    /// <summary><see cref="HttpContext.Items"/> key holding the validated scope.</summary>
    public const string ScopeItem = "impersonation.scope";
}

/// <summary>
/// Enforces the four properties of a consented support session (§7, migration 0014) on every
/// request that carries an <c>imp_grant</c> claim.
///
/// <para><b>Why the token is not trusted.</b> The claim says a grant existed when the token was
/// minted. It cannot say the grant still exists — that a provider has not, thirty seconds ago,
/// pressed "end this session". So the grant is re-read from the database on every single
/// impersonated request. That is a real cost, and it is paid only by support sessions: an ordinary
/// request has no <c>imp_grant</c> claim and this middleware returns immediately.</para>
///
/// <para><b>Read-first.</b> A <c>read_only</c> session is refused any unsafe HTTP method before it
/// reaches a handler. This is a blunt instrument on purpose: an allow-list of "safe" write endpoints
/// is a list somebody eventually adds to, and the value of read-only is that it needs no
/// trust.</para>
///
/// <para><b>Ordering.</b> Runs after authentication (it needs the claims) and before authorization
/// and the endpoint. It must also run before anything that writes: a refusal here must mean nothing
/// happened, not that something happened and was then reported as forbidden.</para>
/// </summary>
public sealed class ImpersonationGuardMiddleware
{
    /// <summary>Sent when a read-only session attempts a write.</summary>
    public const string ReadOnlyMessage =
        "This support session is read-only. The provider granted view access only.";

    /// <summary>Sent when the grant is no longer usable — revoked, expired, never approved, or gone.</summary>
    public const string NotActiveMessage =
        "This support session is no longer active. Ask the provider for a new approval.";

    /// <summary>Sent when the token's grant does not belong to this caller or this brand.</summary>
    public const string MismatchMessage =
        "This support session does not match the signed-in user or brand.";

    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS" };

    private readonly RequestDelegate _next;

    public ImpersonationGuardMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var raw = context.User?.FindFirst(TokenClaims.ImpersonationGrantClaim)?.Value;
        if (string.IsNullOrEmpty(raw))
        {
            await _next(context);
            return;
        }

        // A malformed grant claim is not an ordinary session that happens to have junk in it — it is
        // a token asserting impersonation it cannot substantiate. Refuse.
        if (!Guid.TryParse(raw, out var grantId))
        {
            await DenyAsync(context, NotActiveMessage);
            return;
        }

        var store = context.RequestServices.GetService<IImpersonationStateStore>();
        if (store is null)
        {
            // Fail CLOSED — see IImpersonationStateStore. A host that forgot to register the store
            // must not silently grant unbounded, unverifiable access to customer accounts.
            await DenyAsync(context, NotActiveMessage);
            return;
        }

        ImpersonationState? state;
        try
        {
            state = await store.GetAsync(grantId, context.RequestAborted);
        }
        catch
        {
            await DenyAsync(context, NotActiveMessage);
            return;
        }

        if (state is null || state.Status != ImpersonationGrantStatus.Approved)
        {
            await DenyAsync(context, NotActiveMessage);
            return;
        }

        // The grant names one person and one brand. A token whose subject or brand has drifted from
        // it — a reissue, a scope switch, a copied token — is not the session that was consented to.
        var subject = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                      ?? context.User?.FindFirst("sub")?.Value;
        var brand = context.User?.FindFirst("brand_id")?.Value;
        if (!Guid.TryParse(subject, out var userId) || userId != state.SupportUserId
            || !Guid.TryParse(brand, out var brandId) || brandId != state.BrandId)
        {
            await DenyAsync(context, MismatchMessage);
            return;
        }

        // The DATABASE's scope wins over the token's. An owner who downgrades a session from write
        // to read must not have to wait for a token to expire.
        if (state.Scope == ImpersonationScope.ReadOnly && !SafeMethods.Contains(context.Request.Method))
        {
            await DenyAsync(context, ReadOnlyMessage);
            return;
        }

        context.Items[ImpersonationKeys.GrantIdItem] = grantId;
        context.Items[ImpersonationKeys.ScopeItem] = state.Scope;

        await _next(context);
    }

    private static async Task DenyAsync(HttpContext context, string message)
    {
        // 403, not 401: the caller IS authenticated. What they lack is a live consent, and a 401
        // would send the client off to re-login, which cannot help and loses the reason.
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = false,
            message = new { responseCode = "impersonation_not_permitted", responseMessage = message },
        }));
    }
}
