namespace commerce.Infrastructure.Worker.Options;

/// <summary>Configuration for the Meta WhatsApp Cloud API sender.
/// Bound from <c>Notifications:WhatsApp</c> in appsettings.</summary>
public sealed class WhatsAppOptions
{
    public const string SectionName = "Notifications:WhatsApp";

    /// <summary>Meta graph API access token (long-lived / system user token).</summary>
    public string? AccessToken { get; set; }

    /// <summary>Your WhatsApp Business phone-number-id registered in Meta Business Manager.</summary>
    public string? PhoneNumberId { get; set; }

    /// <summary>Set to false (or omit) to fall back to the logging stub. Default: false.</summary>
    public bool Enabled { get; set; }
}

/// <summary>Configuration for the MSG91 SMS gateway (India DLT-registered).
/// Bound from <c>Notifications:Sms</c> in appsettings.</summary>
public sealed class SmsOptions
{
    public const string SectionName = "Notifications:Sms";

    /// <summary>MSG91 auth key.</summary>
    public string? AuthKey { get; set; }

    /// <summary>DLT-registered sender ID (6-char alpha for transactional routes).</summary>
    public string? SenderId { get; set; }

    /// <summary>DLT template id registered with MSG91 for transactional category.</summary>
    public string? DltTemplateId { get; set; }

    /// <summary>Set to false (or omit) to fall back to the logging stub. Default: false.</summary>
    public bool Enabled { get; set; }
}

/// <summary>Configuration for the Expo push notification sender.
/// Bound from <c>Notifications:Push</c> in appsettings.</summary>
public sealed class PushOptions
{
    public const string SectionName = "Notifications:Push";

    /// <summary>Optional Expo server-side push token for enhanced delivery receipts.
    /// If omitted, unauthenticated requests are made (fine for most volumes).</summary>
    public string? AccessToken { get; set; }

    /// <summary>Set to false (or omit) to fall back to the logging stub. Default: false.</summary>
    public bool Enabled { get; set; }
}
