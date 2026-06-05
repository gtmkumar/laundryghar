using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.SharedDataModel.Entities.IdentityAccess;

namespace laundryghar.SharedDataModel.Entities.FinanceRoyalty;

/// <summary>Daily/shift cash register per store (finance_royalty.cash_books).
/// Has created_at, updated_at, created_by, updated_by — NO version column, so IAuditableEntity is NOT applied.
/// UNIQUE(store_id, book_date, shift_label) — one book per store per shift per day.
/// variance is a generated column: closing_balance - expected_closing.</summary>
public class CashBook
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid FranchiseId { get; set; }
    public Guid StoreId { get; set; }
    public DateOnly BookDate { get; set; }

    /// <summary>CHECK: morning, afternoon, evening, night, full_day.</summary>
    public string ShiftLabel { get; set; } = null!;

    public Guid OpeningUserId { get; set; }
    public Guid? ClosingUserId { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal? ClosingBalance { get; set; }
    public decimal? ExpectedClosing { get; set; }

    /// <summary>Generated column: closing_balance - expected_closing. Read-only — do not set.</summary>
    public decimal? Variance { get; set; }

    public decimal CashInflow { get; set; }
    public decimal CashOutflow { get; set; }
    public decimal UpiInflow { get; set; }
    public decimal CardInflow { get; set; }
    public decimal OtherInflow { get; set; }
    public decimal DepositAmount { get; set; }
    public string? DepositReference { get; set; }
    public int TotalOrders { get; set; }
    public int NewOrders { get; set; }
    public int DeliveredOrders { get; set; }
    public int CancelledOrders { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>CHECK: open, closing, closed, reviewed, disputed, finalized.</summary>
    public string Status { get; set; } = null!;

    public string? VarianceReason { get; set; }
    public string? Notes { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>jsonb — arbitrary metadata.</summary>
    public string Metadata { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Franchise Franchise { get; set; } = null!;
    public Store Store { get; set; } = null!;
    public ICollection<CashBookEntry> Entries { get; set; } = [];
    public ICollection<ShiftHandover> ShiftHandovers { get; set; } = [];
}
