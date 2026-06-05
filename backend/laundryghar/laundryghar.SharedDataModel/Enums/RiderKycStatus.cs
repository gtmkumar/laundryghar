namespace laundryghar.SharedDataModel.Enums;

/// <summary>Valid values for logistics.riders.kyc_status CHECK constraint.</summary>
public static class RiderKycStatus
{
    public const string Pending = "pending";
    public const string Submitted = "submitted";
    public const string Verified = "verified";
    public const string Rejected = "rejected";
    public const string Expired = "expired";
}
