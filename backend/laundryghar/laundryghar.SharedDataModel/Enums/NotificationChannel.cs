namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Delivery channel for notification_templates and notifications_outbox.
/// CHECK (notification_templates.channel): channel IN ('sms','whatsapp','email','push','in_app','voice')
/// notifications_outbox.channel has no DB CHECK constraint; uses the same value set.
/// </summary>
public static class NotificationChannel
{
    public const string Sms       = "sms";
    public const string WhatsApp  = "whatsapp";
    public const string Email     = "email";
    public const string Push      = "push";
    public const string InApp     = "in_app";
    public const string Voice     = "voice";
}
