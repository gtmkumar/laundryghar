using System.Security.Claims;
using laundryghar.Utilities.Auth;
using Microsoft.AspNetCore.Http;

namespace laundryghar.Utilities.Authorization.Abac;

/// <summary>
/// Supplies the subject slice (A2.2) — the attributes that describe WHO is asking.
///
/// <para><b>The rule this class exists to enforce:</b> a key is added only when it can genuinely be
/// resolved. A staff token has no customer, so <c>subject.customer_id</c> is added as a KNOWN null
/// and compares normally. A pre-feature token has no <c>scope_nodes</c> claim at all, so that key is
/// OMITTED and every <c>within_scope</c> comparison over it is Indeterminate — which the PDP turns
/// into a deny. That is the deliberate inversion of
/// <c>HttpContextCurrentUser.IsWithinScope</c>:84, where an absent claim returns <c>true</c> and
/// allows everything (A7.4).</para>
///
/// <para>An unauthenticated principal resolves NOTHING. Every condition then evaluates
/// Indeterminate and the request is denied without a special case anywhere.</para>
/// </summary>
public sealed class SubjectAttributeResolver : IAttributeResolver
{
    public const string ResolverKey = "subject";

    private readonly IHttpContextAccessor _accessor;
    private readonly IAbacSubjectDataSource? _dataSource;

    public SubjectAttributeResolver(
        IHttpContextAccessor accessor, IAbacSubjectDataSource? dataSource = null)
    {
        _accessor = accessor;
        _dataSource = dataSource;
    }

    public string Key => ResolverKey;

    public async ValueTask ResolveAsync(AbacAttributeBag bag, CancellationToken ct)
    {
        var principal = _accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            return; // nothing resolvable → every condition Indeterminate → deny

        var tokenUse = principal.FindFirstValue("token_use");
        bag.Set(AbacAttributeKeys.SubjectTokenUse, tokenUse);
        bag.Set(AbacAttributeKeys.SubjectUserType, principal.FindFirstValue("user_type"));

        // `sub` carries the staff user id on a user token and the customer id on a customer token.
        // Splitting them here is the same distinction HttpContextCurrentTenant draws (A0.6) — and
        // keeping it means a policy over subject.customer_id can never be satisfied by a staff id.
        var subject = ParseGuid(principal, ClaimTypes.NameIdentifier);
        var isCustomer =
            string.Equals(tokenUse, CustomerTokenClaims.TokenUseValue, StringComparison.Ordinal)
         || string.Equals(tokenUse, CustomerTokenClaims.OAuthTokenUseValue, StringComparison.Ordinal);

        bag.Set(AbacAttributeKeys.SubjectUserId, isCustomer ? null : subject);
        bag.Set(AbacAttributeKeys.SubjectCustomerId, isCustomer ? subject : null);

        // The X-Brand-Id override is deliberately NOT read here. It is a platform-admin convenience
        // applied by middleware; letting it feed the subject's brand attribute would make a header
        // able to move the subject across tenants for policy purposes.
        bag.Set(AbacAttributeKeys.SubjectBrandId, ParseGuid(principal, "brand_id"));
        bag.Set(AbacAttributeKeys.SubjectFranchiseId, ParseGuid(principal, "franchise_id"));
        bag.Set(AbacAttributeKeys.SubjectStoreId, ParseGuid(principal, "store_id"));
        bag.Set(AbacAttributeKeys.SubjectPartnerId,
            ParseGuid(principal, PartnerTokenClaims.PartnerIdClaim));

        bag.Set(AbacAttributeKeys.SubjectPermissions,
            SplitClaim(principal, "permissions").Select(PermissionAlias.Canonical).ToArray());

        // scope_nodes: PRESENT (even empty) is enforceable, ABSENT is not resolvable. The mint path
        // always emits it for user tokens, so absent means a pre-feature token — and a pre-feature
        // token must not be silently trusted here the way the legacy check trusts it.
        var scopeNodes = principal.FindFirstValue("scope_nodes");
        if (scopeNodes is not null)
            bag.Set(AbacAttributeKeys.SubjectScopeNodes,
                scopeNodes.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        // stepup_at is unix seconds. Absent = never stepped up, which is a known state, not an
        // unresolved one — so it is set to null rather than omitted, and `is_null` can test it.
        bag.Set(AbacAttributeKeys.SubjectStepUpAt, ReadStepUpAt(principal));

        // Roles ride in the token (TokenClaims.RolesClaim), so this costs no round trip. PRESENT
        // matters here: the A6.3 deny policies read subject.roles, and an unresolved attribute makes
        // an unevaluable deny — which fails closed and would deny everything. A present-but-empty
        // claim states "no roles" and lets those denies correctly not match.
        var roles = principal.FindFirstValue(TokenClaims.RolesClaim);
        if (roles is not null)
            bag.Set(AbacAttributeKeys.SubjectRoles,
                roles.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        // Entitlements are the exception: the token carries `ent_off`, the NEGATIVE list, so the
        // positive set has to be read. Supplied by an optional cached source; with none registered
        // the key stays absent and any policy over it denies.
        if (_dataSource is not null && subject is { } id && !isCustomer)
        {
            var brandId = ParseGuid(principal, "brand_id");
            var facts = await _dataSource.GetAsync(id, brandId, ct);
            if (facts is not null)
            {
                // Only fill roles from the database when the token predates the claim.
                if (roles is null) bag.Set(AbacAttributeKeys.SubjectRoles, facts.RoleCodes);
                bag.Set(AbacAttributeKeys.SubjectEntitlements, facts.Entitlements);
            }
        }
    }

    private static DateTimeOffset? ReadStepUpAt(ClaimsPrincipal principal)
        => long.TryParse(principal.FindFirstValue(TokenClaims.StepUpAtClaim), out var unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix)
            : null;

    private static Guid? ParseGuid(ClaimsPrincipal principal, string claim)
        => Guid.TryParse(principal.FindFirstValue(claim), out var g) ? g : null;

    private static string[] SplitClaim(ClaimsPrincipal principal, string claim)
        => principal.FindFirstValue(claim)?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
           ?? [];
}

/// <summary>The two subject facts that are not in the JWT. Cached by the implementation.</summary>
public sealed record AbacSubjectFacts(string[] RoleCodes, string[] Entitlements);

/// <summary>
/// Supplies <c>subject.roles</c> and <c>subject.entitlements</c>, which the token does not carry.
/// Optional: with no implementation registered both keys are omitted and any policy referencing
/// them denies, which is the correct failure direction.
/// </summary>
public interface IAbacSubjectDataSource
{
    ValueTask<AbacSubjectFacts?> GetAsync(Guid userId, Guid? brandId, CancellationToken ct);
}
