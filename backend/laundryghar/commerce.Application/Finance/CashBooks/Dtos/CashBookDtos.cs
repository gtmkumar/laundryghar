namespace commerce.Application.Finance.CashBooks.Dtos;

// ── Requests ──────────────────────────────────────────────────────────────────

public sealed record OpenCashBookRequest(
    Guid     StoreId,
    Guid     FranchiseId,
    DateOnly BookDate,
    string   ShiftLabel,       // morning|afternoon|evening|night|full_day
    decimal  OpeningBalance);

public sealed record AddCashBookEntryRequest(
    string  EntryType,    // cash_in|cash_out|deposit|withdrawal|adjustment|opening|closing
    string  Category,     // order_payment|refund|expense|salary|utility|rent|maintenance|supply|tip|adjustment|deposit|other
    short   Direction,    // 1 = in, -1 = out
    decimal Amount,
    string  PaymentMode,  // cash|upi|card|bank_transfer|other
    string? Description,
    string? PayeeName,
    string? ReceiptNumber,
    Guid?   ExpenseId);

public sealed record CloseCashBookRequest(
    decimal ClosingBalance,
    string? VarianceReason,
    string? Notes);

public sealed record CreateShiftHandoverRequest(
    Guid     StoreId,
    Guid     FromUserId,
    Guid?    ToUserId,
    decimal  CashHandedOver,
    int      PendingOrdersCount,
    int      OpenComplaintsCount,
    int      PickupsRemaining,
    int      DeliveriesRemaining,
    string?  NotesFrom,
    Guid?    CashBookId);

// ── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record CashBookDto(
    Guid             Id,
    Guid             BrandId,
    Guid             FranchiseId,
    Guid             StoreId,
    DateOnly         BookDate,
    string           ShiftLabel,
    decimal          OpeningBalance,
    decimal?         ClosingBalance,
    decimal?         ExpectedClosing,
    decimal?         Variance,
    decimal          CashInflow,
    decimal          CashOutflow,
    decimal          UpiInflow,
    decimal          CardInflow,
    decimal          OtherInflow,
    decimal          DepositAmount,
    int              TotalOrders,
    string           Status,
    string?          Notes,
    DateTimeOffset   OpenedAt,
    DateTimeOffset?  ClosedAt,
    DateTimeOffset   CreatedAt,
    IReadOnlyList<CashBookEntryDto> Entries);

public sealed record CashBookSummaryDto(
    Guid             Id,
    Guid             StoreId,
    DateOnly         BookDate,
    string           ShiftLabel,
    decimal          OpeningBalance,
    decimal?         ClosingBalance,
    decimal?         Variance,
    decimal          CashInflow,
    decimal          CashOutflow,
    string           Status,
    DateTimeOffset   OpenedAt,
    DateTimeOffset?  ClosedAt);

public sealed record CashBookEntryDto(
    Guid             Id,
    Guid             CashBookId,
    string           EntryType,
    string           Category,
    short            Direction,
    decimal          Amount,
    string           PaymentMode,
    string?          Description,
    string?          PayeeName,
    string?          ReceiptNumber,
    Guid?            ExpenseId,
    DateTimeOffset   OccurredAt,
    DateTimeOffset   CreatedAt);

public sealed record ShiftHandoverDto(
    Guid             Id,
    Guid             StoreId,
    Guid             FromUserId,
    Guid?            ToUserId,
    Guid?            CashBookId,
    DateTimeOffset   HandoverAt,
    decimal          CashHandedOver,
    decimal?         CashVariance,
    string           Status,
    string?          NotesFrom,
    DateTimeOffset   CreatedAt);
