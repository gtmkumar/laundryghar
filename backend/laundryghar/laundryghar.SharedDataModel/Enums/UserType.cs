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

    /// <summary>
    /// The <c>user_type</c> that mirrors a primary role.
    ///
    /// <para><b>Why this exists.</b> Audit finding A-2, "two owners, two shapes": the platform's two
    /// account-creation flows each wrote a user type next to a role code, by hand, and disagreed.
    /// <c>CompleteSignup</c> typed a self-serve brand owner <c>staff</c> while granting them the
    /// <c>brand_admin</c> role; <c>InviteOwner</c> typed a franchise owner <c>franchise_owner</c>
    /// alongside the matching role. Two independent axes, two hand-written pairings, and nothing
    /// checking they agreed — so one of them drifted.</para>
    ///
    /// <para>Type and role remain genuinely different things: a role grants permissions, a type
    /// decides which portal someone lands in and feeds the anti-escalation rank in
    /// <c>SetUserType</c>. What they must not do is contradict each other, because code that reads
    /// the wrong axis then gets the wrong answer silently — <c>AdminSettings.Forbidden</c> gates on
    /// <c>UserType == "brand_admin"</c>, which refused a self-signed-up owner their own brand's
    /// settings.</para>
    ///
    /// <para>Any flow that creates an account together with its primary role calls this instead of
    /// naming a type beside a role code. Roles with no distinct type tier — the vertical operational
    /// roles and any brand-defined custom role — fall through to <see cref="Staff"/>, which is what
    /// they were already.</para>
    /// </summary>
    /// <param name="roleCode">A seeded role code (<c>identity_access.roles.code</c>).</param>
    public static string ForPrimaryRole(string? roleCode) => roleCode switch
    {
        "platform_admin"  => PlatformAdmin,
        "brand_admin"     => BrandAdmin,
        "franchise_owner" => FranchiseOwner,
        "store_admin"     => StoreAdmin,
        "rider"           => Rider,
        "auditor"         => Auditor,
        "support"         => Support,
        "warehouse_staff" => WarehouseStaff,
        _                 => Staff,
    };
}
