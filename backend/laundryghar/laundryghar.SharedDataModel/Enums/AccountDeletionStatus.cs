namespace laundryghar.SharedDataModel.Enums;

public static class AccountDeletionStatus
{
    public const string Pending = "pending";
    public const string GracePeriod = "grace_period";
    public const string SoftDeleted = "soft_deleted";
    public const string HardDeleted = "hard_deleted";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}
