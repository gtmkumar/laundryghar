namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Lifecycle status of a row in engagement_cms.notifications_outbox.
/// CHECK: status IN ('pending','queued','sending','sent','failed','expired','suppressed','cancelled')
/// </summary>
public static class NotificationOutboxStatus
{
    public const string Pending    = "pending";
    public const string Queued     = "queued";
    public const string Sending    = "sending";
    public const string Sent       = "sent";
    public const string Failed     = "failed";
    public const string Expired    = "expired";
    public const string Suppressed = "suppressed";
    public const string Cancelled  = "cancelled";
}
