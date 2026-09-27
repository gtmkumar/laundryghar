using System.Security.Claims;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Auth;
using Microsoft.AspNetCore.Http;

namespace laundryghar.Utilities.Services;

/// <summary><see cref="ICurrentUser"/> backed by HttpContext JWT claims.</summary>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public Guid? UserId => ParseGuid(ClaimTypes.NameIdentifier);
    public string? UserType => Claim("user_type");

    // The JWT bearer handler runs with inbound claim mapping ON (the default), which rewrites the
    // JWT's registered claims to their long WS-Fed URIs: `sub` becomes ClaimTypes.NameIdentifier
    // (which is why UserId above reads the mapped name) and `email` becomes ClaimTypes.Email. The
    // token really does carry `email`, but by the time it reaches the principal the short name is
    // gone — so a plain Claim("email") returned null for EVERY caller.
    //
    // That silently broke step-up (§8): StepUpVerifyHandler derives the OTP identifier from
    // ICurrentUser.Email and threw "No email on file to verify against.", making every high/critical
    // permission — payment.refund, pricing.publish, user.create, brands.update — unreachable.
    // Found while verifying custom domains end to end; see docs/TASKS.md T-10.
    //
    // Reading the raw claim FIRST keeps behaviour identical wherever mapping is off (or a token
    // carries the short name anyway) and only falls back to the mapped name. `phone` has no standard
    // mapping today, but it is given the same treatment so it cannot break the same way later.
    public string? Email => Claim("email") ?? Claim(ClaimTypes.Email);
    public string? Phone => Claim("phone")
                            ?? Claim(ClaimTypes.MobilePhone)
                            ?? Claim(ClaimTypes.HomePhone);
    public Guid? BrandId => ParseGuid("brand_id");
    public Guid? FranchiseId => ParseGuid("franchise_id");
    public Guid? StoreId => ParseGuid("store_id");
    public string? ScopeType => Claim("scope_type");
    public Guid? ScopeId => ParseGuid("scope_id");

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    // A7.3 — the `|| ScopeType == Platform` arm is gone.
    //
    // It made ACTIVE SCOPE a master key: any principal whose active membership happened to be at
    // the platform node became a platform admin for every one of the 25 checks that read this
    // property — IsWithinScope's short-circuit, the subscription and platform-plan commands, the
    // access-people visibility filter, the anti-escalation guard in AssignPermission. A
    // platform-scoped `auditor`, a read-only role, would have passed all of them.
    //
    // It is also redundant with what it was presumably meant to express. Platform membership is
    // already carried in scope_nodes and honoured there: IsWithinScope returns true for a
    // "platform" node on its own merits, and so does authz.within_scope. The difference is that a
    // node is EVIDENCE OF A MEMBERSHIP, whereas scope_type is just which membership the caller
    // happens to be operating through right now — a request-shaped detail, not an authority.
    //
    // Verified against live data before narrowing: exactly one platform-scoped membership exists
    // and its user_type is already platform_admin, so no principal loses access to anything.
    public bool IsPlatformAdmin =>
        UserType == SharedDataModel.Enums.UserType.PlatformAdmin;

    public bool HasPermission(string permissionCode)
    {
        var perms = Claim("permissions");
        if (string.IsNullOrEmpty(perms)) return false;
        return perms.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(permissionCode, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ScopeNode> ScopeNodes
    {
        get
        {
            var raw = Claim("scope_nodes");
            if (string.IsNullOrEmpty(raw)) return Array.Empty<ScopeNode>();
            return raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                      .Select(ScopeNode.TryParse)
                      .Where(n => n.HasValue)
                      .Select(n => n!.Value)
                      .ToArray();
        }
    }

    public bool IsWithinScope(Guid? brandId = null, Guid? franchiseId = null, Guid? storeId = null, Guid? warehouseId = null)
    {
        // Platform operators are unbounded (they already bypass RLS + permission checks).
        if (IsPlatformAdmin) return true;

        // A7.4 — an absent scope_nodes claim now DENIES.
        //
        // It used to return true. The reasoning was rollout safety: the mint path
        // (JwtTokenService.CreateAccessToken) always emits scope_nodes for user tokens — even
        // empty — so an absent claim could only mean a token issued before the feature existed,
        // and those should not be locked out mid-session.
        //
        // That reasoning has expired. Access tokens are short-lived, the claim has been emitted for
        // every user token for many releases, and what the rule actually says is "a token missing
        // its authorization data passes every scope check" — the single broadest fail-open in the
        // authority model, reachable by anything that can present a signed token without the claim.
        // A present-but-empty claim still means "no memberships" and denies via the loop below;
        // that is the case the rollout guard was really protecting, and it is handled without this.
        if (Claim("scope_nodes") is null) return false;

        foreach (var node in ScopeNodes)
        {
            switch (node.ScopeType)
            {
                case SharedDataModel.Enums.ScopeType.Platform:
                    return true; // platform membership is an ancestor of every node
                case SharedDataModel.Enums.ScopeType.Brand when Matches(node.ScopeId, brandId):
                case SharedDataModel.Enums.ScopeType.Franchise when Matches(node.ScopeId, franchiseId):
                case SharedDataModel.Enums.ScopeType.Store when Matches(node.ScopeId, storeId):
                case SharedDataModel.Enums.ScopeType.Warehouse when Matches(node.ScopeId, warehouseId):
                    return true;
            }
        }
        return false;

        static bool Matches(Guid? nodeId, Guid? targetId)
            => nodeId is { } n && targetId is { } t && n == t;
    }

    public Guid? ImpersonationGrantId =>
        Guid.TryParse(Claim(Auth.TokenClaims.ImpersonationGrantClaim), out var g) ? g : null;

    public string? ImpersonationScope => Claim(Auth.TokenClaims.ImpersonationScopeClaim);

    public Guid? TryGetBrandId()
    {
        if (_accessor.HttpContext?.Items.TryGetValue("brand_id_override", out var overrideVal) == true
            && overrideVal is Guid overrideGuid && overrideGuid != Guid.Empty)
            return overrideGuid;

        return BrandId is { } b && b != Guid.Empty ? b : null;
    }

    // F-4 — throws a 400, not a 401. See BrandContextRequiredException for why the distinction
    // matters: the token is valid, the request is simply missing the brand it acts on, and 401
    // makes every client tear down the session over a missing header.
    public Guid RequireBrandId()
        => TryGetBrandId()
           ?? throw new Exceptions.BrandContextRequiredException(
               "Brand context required. For platform admins, pass the X-Brand-Id header.");

    private string? Claim(string type) => Principal?.FindFirstValue(type);
    private Guid? ParseGuid(string type) => Guid.TryParse(Claim(type), out var g) ? g : null;
}
