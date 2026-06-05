namespace laundryghar.SharedDataModel.Enums;

/// <summary>commerce.payments status CHECK constraint values.
/// Named CommercePaymentStatus to avoid conflict with the existing order-lifecycle PaymentStatus enum.</summary>
public static class CommercePaymentStatus
{
    public const string Pending = "pending";
    public const string Initiated = "initiated";
    public const string Authorized = "authorized";
    public const string Captured = "captured";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Refunded = "refunded";
    public const string PartiallyRefunded = "partially_refunded";
    public const string Disputed = "disputed";
}
