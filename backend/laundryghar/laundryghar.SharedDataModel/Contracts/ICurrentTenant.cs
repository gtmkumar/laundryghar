namespace laundryghar.SharedDataModel.Contracts;

/// <summary>
/// Provides the current tenant context for Row-Level Security configuration.
/// Implementations live in the individual service projects, not in this library.
/// </summary>
public interface ICurrentTenant
{
    Guid? BrandId { get; }
    Guid? FranchiseId { get; }
    Guid? StoreId { get; }
    Guid? UserId { get; }

    /// <summary>RaaS partner id — set from the <c>partner_id</c> claim on a <c>token_use=partner</c> JWT.
    /// Drives the <c>rls_partner</c> policy (isolation on <c>partner_id</c>, mirroring brand isolation).
    /// Null for staff / customer / worker sessions.</summary>
    Guid? PartnerId { get; }

    /// <summary>Customer id — set from <c>sub</c> on a <c>token_use=customer</c> (or
    /// <c>customer_mcp</c>) JWT. Drives the customer half of the <c>rls_brand_or_customer</c>
    /// policies on payments, wallets, refunds, loyalty and notification preferences.
    /// Null for staff / rider / partner / worker sessions.
    ///
    /// A0.6: ten policies called <c>kernel.current_customer_id()</c> from the day they were
    /// written, but this property did not exist and the interceptor never set the GUC. Because the
    /// clause is <c>current_customer_id() IS NULL OR customer_id = current_customer_id()</c>, the
    /// unset GUC made the <c>IS NULL</c> arm true and every one of those policies degraded to plain
    /// brand equality — a FAIL-OPEN, not a fail-closed: customer isolation rested entirely on
    /// per-handler ownership checks. Defaulted to null so the non-HTTP tenant adapters (worker,
    /// host) need no change.</summary>
    Guid? CustomerId => null;

    // ── A5.2: subject attributes for authz.permits() ──────────────────────────────────────────
    // All five default to null so the worker and host adapters — which have no request principal —
    // need no change. A null publishes as an empty GUC, which resolves to "no memberships" and
    // therefore denies; see RlsConnectionInterceptor for why unresolved is not representable here.

    /// <summary>users.user_type, for policies that key on the coarse account kind.</summary>
    string? UserType => null;

    /// <summary>user | customer | customer_mcp | partner | api_key — which lane the caller is in.</summary>
    string? TokenUse => null;

    /// <summary>Space-separated ancestor-or-self membership nodes ("platform", "brand:&lt;uuid&gt;"),
    /// verbatim from the JWT claim. Drives <c>authz.within_scope</c> — the franchise/store boundary
    /// no hand-written RLS policy has ever expressed.</summary>
    string? ScopeNodes => null;

    /// <summary>Space-separated effective permission codes from the JWT.</summary>
    string? Permissions => null;

    /// <summary>Space-separated role codes. Null unless a host resolves them; policies over
    /// <c>subject.roles</c> then deny, which is the correct direction for an unknown.</summary>
    string? Roles => null;

    /// <summary>When true the RLS interceptor sets app.bypass_rls = 'true'.</summary>
    bool BypassRls { get; }
}
