namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Delivery outcome status for engagement_cms.notifications_log (partitioned).
/// CHECK: status IN ('sent','delivered','read','clicked','failed','bounced','blocked')
/// </summary>
public static class NotificationLogStatus
{
    public const string Sent      = "sent";
    public const string Delivered = "delivered";
    public const string Read      = "read";
    public const string Clicked   = "clicked";
    public const string Failed    = "failed";
    public const string Bounced   = "bounced";
    public const string Blocked   = "blocked";
}
