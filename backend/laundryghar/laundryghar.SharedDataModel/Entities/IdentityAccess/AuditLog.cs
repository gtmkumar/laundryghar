using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.IdentityAccess;

/// <summary>Partitioned audit event log (identity_access.audit_logs).
/// Composite PK (Id, OccurredAt) — required by PG range partitioning.
/// Has created_at, created_by only — no updated_at, no version, no deleted_at.</summary>
public class AuditLog
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? BrandId { get; set; }
    public Guid? FranchiseId { get; set; }
    public Guid? StoreId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? ActorUserId { get; set; }

    /// <summary>FK to customer_catalog.customers — cross-BC, scalar only.</summary>
    public Guid? ActorCustomerId { get; set; }

    public string ActorType { get; set; } = null!;
    public string? ActorDisplay { get; set; }
    public string Action { get; set; } = null!;
    public string ResourceType { get; set; } = null!;
    public Guid? ResourceId { get; set; }
    public string? ResourceDisplay { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string[]? ChangedFields { get; set; }
    public IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public Guid? RequestId { get; set; }
    public Guid? CorrelationId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations — within tenancy_org BC
    public Brand? Brand { get; set; }
    public Franchise? Franchise { get; set; }
    public Store? Store { get; set; }
    public Warehouse? Warehouse { get; set; }
}
