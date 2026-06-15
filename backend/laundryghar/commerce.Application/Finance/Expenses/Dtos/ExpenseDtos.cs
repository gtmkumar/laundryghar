namespace commerce.Application.Finance.Expenses.Dtos;

// ── Requests ──────────────────────────────────────────────────────────────────

public sealed record CreateExpenseCategoryRequest(
    string   Code,
    string   Name,
    Guid?    ParentId,
    string?  Description,
    bool     IsTaxDeductible,
    bool     RequiresApproval,
    decimal? ApprovalThreshold,
    string?  AccountingCode,
    short    DisplayOrder);

public sealed record UpdateExpenseCategoryRequest(
    string   Name,
    string?  Description,
    bool     IsTaxDeductible,
    bool     RequiresApproval,
    decimal? ApprovalThreshold,
    string?  AccountingCode,
    short    DisplayOrder,
    string   Status);   // active|inactive|archived

public sealed record CreateExpenseRequest(
    Guid     FranchiseId,
    Guid?    StoreId,
    Guid?    WarehouseId,
    Guid     CategoryId,
    DateOnly ExpenseDate,
    decimal  Amount,
    decimal  TaxAmount,
    string   PaymentMode,     // cash|upi|card|bank_transfer|cheque|credit
    string   Description,
    string?  VendorName,
    string?  VendorGstin,
    string?  VendorPhone,
    string?  BillNumber,
    DateOnly? BillDate,
    string?  Notes,
    bool     IsRecurring,
    string?  RecurrenceFrequency,  // weekly|monthly|quarterly|yearly
    bool     IsReimbursable,
    bool     RequiresApproval,
    bool     SubmitNow);      // false = draft, true = submitted

public sealed record ApproveExpenseRequest(string? Notes);
public sealed record RejectExpenseRequest(string RejectionReason);
public sealed record MarkExpensePaidRequest(string? Notes);

public sealed record AddExpenseAttachmentRequest(
    string  S3Key,
    string  FileName,
    string  MimeType,
    int?    Bytes,
    string? DocumentType,   // receipt|invoice|bill|quotation|other
    bool    IsPrimary,
    string? CdnUrl);

// ── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record ExpenseCategoryDto(
    Guid     Id,
    Guid     BrandId,
    Guid?    ParentId,
    string   Code,
    string   Name,
    string?  Description,
    bool     IsTaxDeductible,
    bool     RequiresApproval,
    decimal? ApprovalThreshold,
    string?  AccountingCode,
    short    DisplayOrder,
    bool     IsActive,
    string   Status,
    DateTimeOffset CreatedAt);

public sealed record ExpenseDto(
    Guid             Id,
    Guid             BrandId,
    Guid             FranchiseId,
    Guid?            StoreId,
    Guid             CategoryId,
    string           CategoryName,
    string           ExpenseNumber,
    DateOnly         ExpenseDate,
    decimal          Amount,
    decimal          TaxAmount,
    decimal?         TotalAmount,
    string           PaymentMode,
    string?          VendorName,
    string?          BillNumber,
    string           Description,
    string?          Notes,
    bool             IsRecurring,
    string?          RecurrenceFrequency,
    bool             IsReimbursable,
    string           Status,
    DateTimeOffset   SubmittedAt,
    DateTimeOffset?  ApprovedAt,
    DateTimeOffset?  PaidAt,
    string?          RejectionReason,
    DateTimeOffset   CreatedAt,
    IReadOnlyList<ExpenseAttachmentDto> Attachments);

public sealed record ExpenseAttachmentDto(
    Guid    Id,
    Guid    ExpenseId,
    string  S3Key,
    string  FileName,
    string  MimeType,
    int?    Bytes,
    string? DocumentType,
    bool    IsPrimary,
    string? CdnUrl,
    DateTimeOffset UploadedAt);
