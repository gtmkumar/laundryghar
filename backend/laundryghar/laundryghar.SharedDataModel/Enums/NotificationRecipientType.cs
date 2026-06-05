namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Who the notification is addressed to (notifications_outbox.recipient_type).
/// CHECK: recipient_type IN ('customer','user','rider','franchisee','manual')
/// notifications_log.recipient_type has no DB CHECK constraint; uses the same value set.
/// </summary>
public static class NotificationRecipientType
{
    public const string Customer    = "customer";
    public const string User        = "user";
    public const string Rider       = "rider";
    public const string Franchisee  = "franchisee";
    public const string Manual      = "manual";
}
