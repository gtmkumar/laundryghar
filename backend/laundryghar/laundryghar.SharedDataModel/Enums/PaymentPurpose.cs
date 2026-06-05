namespace laundryghar.SharedDataModel.Enums;

/// <summary>commerce.payments payment_purpose CHECK constraint values.</summary>
public static class PaymentPurpose
{
    public const string Order = "order";
    public const string Package = "package";
    public const string WalletTopup = "wallet_topup";
    public const string Tip = "tip";
    public const string Adjustment = "adjustment";
    public const string Refund = "refund";
    public const string Royalty = "royalty";
}
