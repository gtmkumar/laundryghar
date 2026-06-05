namespace laundryghar.SharedDataModel.Enums;

/// <summary>Valid values for logistics.rider_assignments.status CHECK constraint.</summary>
public static class RiderAssignmentStatus
{
    public const string Scheduled = "scheduled";
    public const string Active = "active";
    public const string OnBreak = "on_break";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string NoShow = "no_show";
}
