namespace laundryghar.SharedDataModel.Entities.IdentityAccess;

/// <summary>PaaS entitlement: which navigator modules a brand has licensed
/// (identity_access.brand_module). Effective access = entitlement ∩ authorization.
/// Brand-scoped (RLS). Core modules (<see cref="AppModule.IsCore"/>) bypass this.
/// See docs/rbac-entitlement-plan.md.</summary>
public class BrandModule
{
    public Guid BrandId { get; set; }
    public string ModuleKey { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    /// <summary>NULL = perpetual; a past date = expired (treated as not entitled).</summary>
    public DateOnly? ValidUntil { get; set; }
    /// <summary>'bundle' = applied from a plan; 'manual' = per-brand add-on/exception.</summary>
    public string Source { get; set; } = "manual";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
