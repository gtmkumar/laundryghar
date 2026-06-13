namespace laundryghar.SharedDataModel.Enums;

public static class DeliveryAssignmentStatus
{
    /// <summary>Offered to a rider in offer_accept dispatch mode; awaiting accept/decline/expiry.</summary>
    public const string Offered = "offered";
    public const string Assigned = "assigned";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string Started = "started";
    public const string Arrived = "arrived";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
    public const string Rescheduled = "rescheduled";
    /// <summary>An offer that lapsed without acceptance (offer_accept mode).</summary>
    public const string Expired = "expired";
}
