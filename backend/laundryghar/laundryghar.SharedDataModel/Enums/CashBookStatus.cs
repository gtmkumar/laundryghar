namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Matches finance_royalty.cash_books.status CHECK constraint exactly.
/// Values: open, closing, closed, reviewed, disputed, finalized.
/// </summary>
public enum CashBookStatus
{
    Open,
    Closing,
    Closed,
    Reviewed,
    Disputed,
    Finalized
}
