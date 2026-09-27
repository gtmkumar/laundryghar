namespace laundryghar.Utilities.Auth;

/// <summary>
/// Claims for a system user (staff/admin/rider) JWT.
/// token_use is always "user" — used to reject customer tokens on admin endpoints.
/// </summary>
public sealed record TokenClaims(
    Guid UserId,
    string UserType,
    string? Email,
    string? Phone,
    // Active scope (from X-Scope header or primary membership)
    string? ScopeType,
    Guid? ScopeId,
    Guid? BrandId,
    Guid? FranchiseId,
    Guid? StoreId,
    // Space-separated permission codes
    string Permissions,
    // Snapshot of the user's perm_version at issuance (for live revocation). Default 0.
    int PermVersion = 0,
    // Space-separated "type:id" scope nodes the user holds (platform → "platform").
    // Enables the per-request §6 ancestor-or-self boundary check (ICurrentUser.IsWithinScope).
    string? ScopeNodes = null,
    // Space-separated high/critical permission codes the caller must step up for (§8).
    // Always emitted for system users; read by the authz handlers to decide if step-up applies.
    string? StepUpPerms = null,
    // Authentication method reference of a fresh step-up (e.g. "otp"); emitted ONLY on a token
    // re-issued by /auth/step-up/verify — null on login/refresh.
    string? Amr = null,
    // Unix seconds of the successful step-up verify; emitted ONLY on the upgraded token. The proof
    // is fresh while now − StepUpAt ≤ StepUp.FreshnessWindow.
    long? StepUpAt = null,
    // Space-separated FEATURE keys this brand has NOT licensed (PLATFORM_STRATEGY.md §5). Emitted
    // only when entitlement enforcement is on and the caller is brand-scoped. Bounded by the size of
    // the feature catalogue (tens of short keys), unlike the stripped permission set it explains,
    // which can run to hundreds.
    //
    // Its whole purpose is to make "you do not own this" distinguishable from "you are not allowed
    // this". The entitlement filter removes un-entitled permissions from Permissions above, so
    // without this claim BOTH cases arrive at the authorization handler as an identical absent
    // permission and both would answer 403 — telling a paying customer they lack permission when
    // what they actually lack is the plan. With it, the result handler answers 402 and names the
    // feature to upgrade.
    string? EntitlementOff = null,
    // The consent this token was issued under (§7). Present ONLY on a token minted by
    // /admin/impersonation/{id}/start; absent means "this is an ordinary session".
    //
    // The token deliberately keeps the SUPPORT engineer's `sub`. A token that claimed to be the
    // provider would make the audit trail lie about who acted, which is the one thing an
    // impersonation feature must never do. What changes is brand_id (so RLS scopes to the tenant)
    // and these two claims (so the guard knows the session is bounded and read-first).
    Guid? ImpersonationGrantId = null,
    // read_only | read_write. Advisory only — the guard re-reads the authoritative scope from the
    // database each request, because a claim minted an hour ago cannot know it has been revoked.
    string? ImpersonationScope = null,
    // Space-separated ROLE codes held via ancestor-or-self memberships. Distinct from Permissions:
    // a role is what a policy names, a permission is what it grants, and the two have already
    // drifted (docs/AUTHORITY_MODEL.md). ABAC needs the role itself as a subject attribute — the
    // A6.3 deny policies are written as "subject.roles contains <role>" — and without it in the
    // token the attribute would have to be resolved with a database round trip per request, or, in
    // the RLS layer, be unresolvable and therefore make every deny policy fire.
    //
    // Bounded by the number of memberships a person holds (a handful), not by the permission
    // catalogue, so this costs the token very little.
    string? Roles = null
)
{
    /// <summary>JWT claim name carrying <see cref="Roles"/>.</summary>
    public const string RolesClaim = "roles";

    /// <summary>Fixed token_use value for system users. Pinned for Catalog service contract.</summary>
    public const string TokenUseValue = "user";

    /// <summary>JWT claim name carrying <see cref="PermVersion"/>.</summary>
    public const string PermVersionClaim = "perm_ver";

    /// <summary>JWT claim name carrying the space-separated high/critical codes (<see cref="StepUpPerms"/>).</summary>
    public const string StepUpPermsClaim = "step_up_perms";

    /// <summary>JWT claim name carrying the step-up auth-method reference (<see cref="Amr"/>).</summary>
    public const string AmrClaim = "amr";

    /// <summary>JWT claim name carrying the step-up timestamp in unix seconds (<see cref="StepUpAt"/>).</summary>
    public const string StepUpAtClaim = "stepup_at";

    /// <summary>JWT claim name carrying the un-licensed feature keys (<see cref="EntitlementOff"/>).</summary>
    public const string EntitlementOffClaim = "ent_off";

    /// <summary>JWT claim name carrying the impersonation grant id (<see cref="ImpersonationGrantId"/>).</summary>
    public const string ImpersonationGrantClaim = "imp_grant";

    /// <summary>JWT claim name carrying the impersonation scope (<see cref="ImpersonationScope"/>).</summary>
    public const string ImpersonationScopeClaim = "imp_scope";
}

/// <summary>
/// Claims for a customer JWT.
/// token_use is always "customer" — used to reject system tokens on customer endpoints.
/// Pinned contract: sub=customer_id, token_use=customer, brand_id, phone. No permissions claim.
/// </summary>
/// <param name="Phone">
/// Null for a customer who signed up with Google and has not linked a phone number yet.
/// The <c>phone</c> claim is then omitted from the JWT entirely rather than emitted empty —
/// downstream services must key off <c>sub</c>, never <c>phone</c>.
/// </param>
public sealed record CustomerTokenClaims(
    Guid CustomerId,
    Guid BrandId,
    string? Phone
)
{
    /// <summary>Fixed token_use value for customers. Pinned for Catalog service contract.</summary>
    public const string TokenUseValue = "customer";

    /// <summary>
    /// token_use value for customers authenticated via OAuth 2.1 (MCP path).
    /// These tokens also carry a <c>scope</c> claim (e.g., "mcp:booking") and are
    /// rejected by Catalog/Orders endpoints (which only accept token_use=customer).
    /// </summary>
    public const string OAuthTokenUseValue = "customer_mcp";
}

/// <summary>
/// Claims for a RaaS partner JWT (docs/rbac.md §9). A partner is a SEPARATE actor — not staff, not
/// customer: the token carries <c>partner_id</c> (the RLS isolation key, mirroring brand_id) and a
/// coarse <c>partner_role</c> (partner_admin | partner_operator), and NO permissions claim. It must
/// NOT carry brand_id or grant bypass, so brand/staff RLS + the staff PermissionHandler stay inert
/// for partner tokens; only the <c>rls_partner</c> policy governs partner visibility.
/// </summary>
public sealed record PartnerTokenClaims(
    Guid PartnerUserId,
    Guid PartnerId,
    string PartnerRole,
    string? Phone
)
{
    /// <summary>Fixed token_use value for partners. Gated by PartnerOnlyRequirement.</summary>
    public const string TokenUseValue = "partner";

    /// <summary>JWT claim carrying the partner id (drives ICurrentTenant.PartnerId → rls_partner).</summary>
    public const string PartnerIdClaim = "partner_id";

    /// <summary>JWT claim carrying the coarse partner role (partner_admin | partner_operator).</summary>
    public const string PartnerRoleClaim = "partner_role";
}

/// <summary>The two RaaS partner roles (a coarse claim, not staff role_permissions).</summary>
public static class PartnerRole
{
    public const string Admin = "partner_admin";
    public const string Operator = "partner_operator";
}
