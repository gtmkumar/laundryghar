namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// The coarse account/role type used for auth scope resolution. Most values are vertical-neutral;
/// <see cref="WarehouseStaff"/> is the laundry-specific operational-staff label retained for data
/// compatibility (seeded users/roles + DB CHECKs reference <c>warehouse_staff</c>). New verticals
/// should use the neutral <see cref="OpsStaff"/> for their on-site processing/service staff.
/// (Neutralized in multi-vertical Phase 2 / slice 2D.)
/// </summary>
public static class UserType
{
    public const string PlatformAdmin = "platform_admin";
    public const string BrandAdmin = "brand_admin";
    public const string FranchiseOwner = "franchise_owner";
    public const string StoreAdmin = "store_admin";
    public const string Staff = "staff";

    /// <summary>Laundry-specific operational staff (warehouse wash/QC). Retained for data
    /// compatibility; prefer <see cref="OpsStaff"/> for vertical-neutral processing staff.</summary>
    public const string WarehouseStaff = "warehouse_staff";

    /// <summary>Vertical-neutral on-site operational/processing staff (salon stylist,
    /// logistics hub operator, …) — the generic successor to <see cref="WarehouseStaff"/>.</summary>
    public const string OpsStaff = "ops_staff";

    public const string Rider = "rider";
    public const string Auditor = "auditor";
    public const string Support = "support";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        PlatformAdmin, BrandAdmin, FranchiseOwner, StoreAdmin, Staff,
        WarehouseStaff, OpsStaff, Rider, Auditor, Support,
    };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);

    /// <summary>True if the type is on-site operational/processing staff in any vertical
    /// (laundry <c>warehouse_staff</c> or the neutral <c>ops_staff</c>).</summary>
    public static bool IsOperationalStaff(string? value)
        => value is WarehouseStaff or OpsStaff;
}
