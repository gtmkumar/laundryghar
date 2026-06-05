namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Matches finance_royalty.cash_book_entries.entry_type CHECK constraint exactly.
/// Values: cash_in, cash_out, deposit, withdrawal, adjustment, opening, closing.
/// </summary>
public enum CashBookEntryType
{
    CashIn,
    CashOut,
    Deposit,
    Withdrawal,
    Adjustment,
    Opening,
    Closing
}
