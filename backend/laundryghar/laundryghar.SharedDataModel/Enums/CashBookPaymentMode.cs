namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Matches finance_royalty.cash_book_entries.payment_mode CHECK constraint exactly.
/// Values: cash, upi, card, bank_transfer, other.
/// </summary>
public enum CashBookPaymentMode
{
    Cash,
    Upi,
    Card,
    BankTransfer,
    Other
}
