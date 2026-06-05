namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Common status for engagement_cms content entities (app_banners, mobile_app_config,
/// notification_preferences, notification_templates, onboarding_slides).
/// CHECK: status IN ('active','inactive','archived')
/// </summary>
public static class CmsStatus
{
    public const string Active   = "active";
    public const string Inactive = "inactive";
    public const string Archived = "archived";
}
