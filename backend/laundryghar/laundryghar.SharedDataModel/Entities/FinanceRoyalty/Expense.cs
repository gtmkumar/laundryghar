using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.FinanceRoyalty;

/// <summary>Expense record (finance_royalty.expenses).
/// Has created_at, updated_at, deleted_at, created_by, updated_by — NO version.
/// ISoftDeletable is applied; IAuditableEntity is NOT (no version column).
/// UNIQUE(expense_number).
/// total_amount is a generated column: amount + tax_amount — read-only.</summary>
public class Expense : ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid FranchiseId { get; set; }
    public Guid? StoreId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid CategoryId { get; set; }
    public Guid? CashBookEntryId { get; set; }
    public string ExpenseNumber { get; set; } = null!;
    public DateOnly ExpenseDate { get; set; }
    public decimal Amount { get; set; }
    public decimal TaxAmount { get; set; }

    /// <summary>Generated column: amount + tax_amount. Read-only — do not set.</summary>
    public decimal? TotalAmount { get; set; }

    /// <summary>CHECK: cash, upi, card, bank_transfer, cheque, credit.</summary>
    public string PaymentMode { get; set; } = null!;

    public string? VendorName { get; set; }
    public string? VendorGstin { get; set; }
    public string? VendorPhone { get; set; }
    public string? BillNumber { get; set; }
    public DateOnly? BillDate { get; set; }
    public string Description { get; set; } = null!;
    public string? Notes { get; set; }
    public bool IsRecurring { get; set; }

    /// <summary>CHECK: weekly, monthly, quarterly, yearly. Null when is_recurring = false.</summary>
    public string? RecurrenceFrequency { get; set; }

    public bool IsReimbursable { get; set; }
    public Guid SubmittedBy { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public bool RequiresApproval { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? RejectedBy { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>CHECK: draft, submitted, approved, rejected, paid, reconciled, disputed.</summary>
    public string Status { get; set; } = null!;

    /// <summary>jsonb — arbitrary metadata.</summary>
    public string Metadata { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Franchise Franchise { get; set; } = null!;
    public Store? Store { get; set; }
    public Warehouse? Warehouse { get; set; }
    public ExpenseCategory Category { get; set; } = null!;
    public CashBookEntry? CashBookEntry { get; set; }
    public ICollection<ExpenseAttachment> Attachments { get; set; } = [];
}
