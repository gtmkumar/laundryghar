namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Message type for engagement_cms.whatsapp_message_log.
/// CHECK: message_type IN ('text','template','image','document','audio','video','button','list','location','contact')
/// </summary>
public static class WhatsAppMessageType
{
    public const string Text     = "text";
    public const string Template = "template";
    public const string Image    = "image";
    public const string Document = "document";
    public const string Audio    = "audio";
    public const string Video    = "video";
    public const string Button   = "button";
    public const string List     = "list";
    public const string Location = "location";
    public const string Contact  = "contact";
}
