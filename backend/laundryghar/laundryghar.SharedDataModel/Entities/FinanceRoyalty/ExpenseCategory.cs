using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.FinanceRoyalty;

/// <summary>Expense category hierarchy (finance_royalty.expense_categories).
/// Has created_at, updated_at, created_by, updated_by — NO version column.
/// UNIQUE(brand_id, code).
/// Self-referencing parent_id for hierarchy.</summary>
public class ExpenseCategory
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid? ParentId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;

    /// <summary>jsonb — localised name map.</summary>
    public string NameLocalized { get; set; } = null!;

    public string? Description { get; set; }
    public bool IsTaxDeductible { get; set; }
    public bool RequiresApproval { get; set; }
    public decimal? ApprovalThreshold { get; set; }
    public string? AccountingCode { get; set; }
    public string? IconUrl { get; set; }
    public short DisplayOrder { get; set; }
    public bool IsActive { get; set; }

    /// <summary>CHECK: active, inactive, archived.</summary>
    public string Status { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public ExpenseCategory? Parent { get; set; }
    public ICollection<ExpenseCategory> Children { get; set; } = [];
    public ICollection<Expense> Expenses { get; set; } = [];
}
