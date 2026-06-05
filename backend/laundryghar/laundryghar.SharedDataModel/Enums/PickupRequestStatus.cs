namespace laundryghar.SharedDataModel.Enums;

public static class PickupRequestStatus
{
    public const string Pending = "pending";
    public const string Assigned = "assigned";
    public const string RiderDispatched = "rider_dispatched";
    public const string Arrived = "arrived";
    public const string Completed = "completed";
    public const string Converted = "converted";
    public const string Cancelled = "cancelled";
    public const string NoResponse = "no_response";
    public const string Rescheduled = "rescheduled";
}
