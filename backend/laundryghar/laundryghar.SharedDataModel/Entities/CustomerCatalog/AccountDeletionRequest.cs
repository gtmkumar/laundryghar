using laundryghar.SharedDataModel.Entities.IdentityAccess;

namespace laundryghar.SharedDataModel.Entities.CustomerCatalog;

/// <summary>DPDP-compliant account deletion request (customer_catalog.account_deletion_requests).
/// Has created_at, created_by — no updated_at, no version, no deleted_at.</summary>
public class AccountDeletionRequest
{
    public Guid Id { get; set; }
    public Guid? CustomerId { get; set; }

    /// <summary>FK to identity_access.users — cross-BC, nav provided since IdentityAccess is in scope.</summary>
    public Guid? UserId { get; set; }

    public Guid BrandId { get; set; }
    public string RequestSource { get; set; } = null!;
    public string? Reason { get; set; }
    public string? ReasonText { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset GracePeriodEndsAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelledReason { get; set; }
    public DateTimeOffset? SoftDeletedAt { get; set; }
    public DateTimeOffset? HardDeletedAt { get; set; }
    public DateTimeOffset? AnonymizedAt { get; set; }
    public string Status { get; set; } = null!;
    public int PendingOrdersCount { get; set; }
    public decimal PendingAmount { get; set; }
    public string? DataExportUrl { get; set; }
    public DateTimeOffset? DataExportExpiresAt { get; set; }
    public IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public Guid? ProcessedBy { get; set; }
    public string? Notes { get; set; }
    public string Metadata { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Customer? Customer { get; set; }
    public User? User { get; set; }
}
