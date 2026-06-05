namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Matches finance_royalty.royalty_invoices.status CHECK constraint exactly.
/// Values: draft, issued, sent, viewed, partial, paid, overdue, disputed, void.
/// </summary>
public enum RoyaltyInvoiceStatus
{
    Draft,
    Issued,
    Sent,
    Viewed,
    Partial,
    Paid,
    Overdue,
    Disputed,
    Void
}
