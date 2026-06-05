using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;

namespace laundryghar.SharedDataModel.Entities.FinanceRoyalty;

/// <summary>Append-only cash ledger line (finance_royalty.cash_book_entries).
/// Has created_at, created_by ONLY — no updated_at, no version, no deleted_at.
/// direction: 1 = in, -1 = out (smallint CHECK).
/// order_id + order_created_at → composite FK to order_lifecycle.orders(id, created_at) ON DELETE RESTRICT.</summary>
public class CashBookEntry
{
    public Guid Id { get; set; }
    public Guid CashBookId { get; set; }
    public Guid BrandId { get; set; }
    public Guid StoreId { get; set; }

    /// <summary>CHECK: cash_in, cash_out, deposit, withdrawal, adjustment, opening, closing.</summary>
    public string EntryType { get; set; } = null!;

    /// <summary>CHECK: order_payment, refund, expense, salary, utility, rent, maintenance, supply, tip, adjustment, deposit, other.</summary>
    public string Category { get; set; } = null!;

    /// <summary>1 = in, -1 = out.</summary>
    public short Direction { get; set; }

    public decimal Amount { get; set; }

    /// <summary>CHECK: cash, upi, card, bank_transfer, other.</summary>
    public string PaymentMode { get; set; } = null!;

    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }

    /// <summary>Part of composite FK to order_lifecycle.orders(id, created_at).</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Partition-key column carried for composite FK.</summary>
    public DateTimeOffset? OrderCreatedAt { get; set; }

    public Guid? ExpenseId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? PayeeName { get; set; }
    public string? Description { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? ReceiptS3Key { get; set; }
    public Guid PerformedBy { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public Guid? ReversedBy { get; set; }
    public string? ReversedReason { get; set; }

    /// <summary>jsonb — arbitrary metadata.</summary>
    public string Metadata { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public CashBook CashBook { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
    public Store Store { get; set; } = null!;
    public Expense? Expense { get; set; }
    public Customer? Customer { get; set; }
}
