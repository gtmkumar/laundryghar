namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Matches finance_royalty.expense_attachments.document_type CHECK constraint exactly.
/// Values: receipt, invoice, bill, quotation, other.
/// </summary>
public enum ExpenseAttachmentDocumentType
{
    Receipt,
    Invoice,
    Bill,
    Quotation,
    Other
}
