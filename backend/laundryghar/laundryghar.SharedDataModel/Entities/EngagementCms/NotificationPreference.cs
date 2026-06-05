using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.EngagementCms;

/// <summary>Per-category channel opt-in/out preferences for a customer or user
/// (engagement_cms.notification_preferences).
/// Has created_at, updated_at, created_by, updated_by — no version, no deleted_at.
/// CHECK: (customer_id IS NOT NULL) OR (user_id IS NOT NULL)</summary>
public class NotificationPreference
{
    public Guid Id { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? UserId { get; set; }
    public Guid BrandId { get; set; }
    public string NotificationCategory { get; set; } = null!;
    public bool SmsEnabled { get; set; }
    public bool WhatsAppEnabled { get; set; }
    public bool EmailEnabled { get; set; }
    public bool PushEnabled { get; set; }
    public bool InAppEnabled { get; set; }
    public bool VoiceEnabled { get; set; }

    /// <summary>Local time-of-day start of the quiet window (time without time zone).</summary>
    public TimeOnly? QuietHoursStart { get; set; }

    /// <summary>Local time-of-day end of the quiet window (time without time zone).</summary>
    public TimeOnly? QuietHoursEnd { get; set; }

    public string? Timezone { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Customer? Customer { get; set; }
    public User? User { get; set; }
}
