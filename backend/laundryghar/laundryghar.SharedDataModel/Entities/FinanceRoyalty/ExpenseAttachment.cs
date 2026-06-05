using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.FinanceRoyalty;

/// <summary>Attachment/receipt file for an expense (finance_royalty.expense_attachments).
/// Has created_at, created_by, deleted_at ONLY — no updated_at, no version.
/// ISoftDeletable applied; IAuditableEntity NOT applied.</summary>
public class ExpenseAttachment : ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid ExpenseId { get; set; }
    public Guid BrandId { get; set; }
    public string S3Key { get; set; } = null!;
    public string? ThumbnailS3Key { get; set; }
    public string? CdnUrl { get; set; }
    public string FileName { get; set; } = null!;
    public string MimeType { get; set; } = null!;
    public int? Bytes { get; set; }

    /// <summary>CHECK: receipt, invoice, bill, quotation, other.</summary>
    public string? DocumentType { get; set; }

    public bool IsPrimary { get; set; }
    public Guid? UploadedBy { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Expense Expense { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
}
