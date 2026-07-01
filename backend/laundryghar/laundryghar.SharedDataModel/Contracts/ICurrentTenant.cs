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

    /// <summary>When true the RLS interceptor sets app.bypass_rls = 'true'.</summary>
    bool BypassRls { get; }
}
