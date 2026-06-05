namespace laundryghar.SharedDataModel.Enums;

/// <summary>commerce.wallet_transactions transaction_type CHECK constraint values.</summary>
public static class WalletTransactionType
{
    public const string Topup = "topup";
    public const string Debit = "debit";
    public const string Refund = "refund";
    public const string Cashback = "cashback";
    public const string Bonus = "bonus";
    public const string Adjustment = "adjustment";
    public const string Reversal = "reversal";
    public const string Lock = "lock";
    public const string Unlock = "unlock";
}
