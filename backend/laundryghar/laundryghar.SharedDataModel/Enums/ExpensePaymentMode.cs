namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Matches finance_royalty.expenses.payment_mode CHECK constraint exactly.
/// Values: cash, upi, card, bank_transfer, cheque, credit.
/// NOTE: expenses has cheque and credit; cash_book_entries does not — these are distinct enums.
/// </summary>
public enum ExpensePaymentMode
{
    Cash,
    Upi,
    Card,
    BankTransfer,
    Cheque,
    Credit
}
