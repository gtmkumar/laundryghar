namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Screen placement of a banner in engagement_cms.app_banners.
/// CHECK: placement IN ('home_top','home_middle','home_bottom','services_top','cart_top','order_success','profile')
/// </summary>
public static class AppBannerPlacement
{
    public const string HomeTop      = "home_top";
    public const string HomeMiddle   = "home_middle";
    public const string HomeBottom   = "home_bottom";
    public const string ServicesTop  = "services_top";
    public const string CartTop      = "cart_top";
    public const string OrderSuccess = "order_success";
    public const string Profile      = "profile";
}
