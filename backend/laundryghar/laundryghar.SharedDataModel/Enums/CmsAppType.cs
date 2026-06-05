namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Target app type for engagement_cms content (onboarding_slides, app_banners, mobile_app_config).
/// onboarding_slides CHECK: app_type IN ('customer','rider','staff','pos')
/// app_banners / mobile_app_config: no DB CHECK constraint on app_type; uses the same value set.
/// </summary>
public static class CmsAppType
{
    public const string Customer = "customer";
    public const string Rider    = "rider";
    public const string Staff    = "staff";
    public const string Pos      = "pos";
}
