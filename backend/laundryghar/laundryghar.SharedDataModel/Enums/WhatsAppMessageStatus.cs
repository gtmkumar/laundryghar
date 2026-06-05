namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Delivery status for engagement_cms.whatsapp_message_log.
/// CHECK: status IN ('sent','delivered','read','failed','received')
/// </summary>
public static class WhatsAppMessageStatus
{
    public const string Sent      = "sent";
    public const string Delivered = "delivered";
    public const string Read      = "read";
    public const string Failed    = "failed";
    public const string Received  = "received";
}
