namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Matches finance_royalty.cash_book_entries.category CHECK constraint exactly.
/// Values: order_payment, refund, expense, salary, utility, rent, maintenance,
///         supply, tip, adjustment, deposit, other.
/// </summary>
public enum CashBookEntryCategory
{
    OrderPayment,
    Refund,
    Expense,
    Salary,
    Utility,
    Rent,
    Maintenance,
    Supply,
    Tip,
    Adjustment,
    Deposit,
    Other
}
