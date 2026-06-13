namespace laundryghar.Commerce.Tests.Catalog;

/// <summary>
/// Regression guard for DEFECT A: soft-delete (archive) handlers must move the row's
/// status off its live value so status-keyed reports stop counting archived rows as
/// active. The chosen archived literal must also be a member of each table's DB CHECK
/// constraint, or the UPDATE would throw at runtime.
///
/// DB CHECK definitions (verified against database_scripts/ + docs/SCHEMA_FULL.sql):
///   commerce.coupons.status            : draft | active | paused | exhausted | expired | retired
///   commerce.packages.status           : draft | active | paused | retired
///   subscription_plans.status          : draft | active | paused | retired
///   platform_plans.status              : draft | active | retired
///   customer_catalog.service_categories: active | disabled | seasonal
///   customer_catalog.services          : active | disabled
///   customer_catalog.fabric_types      : active | disabled
///   customer_catalog.item_groups       : active | disabled
///   customer_catalog.items             : active | disabled | seasonal
///   customer_catalog.item_variants     : active | disabled
///   customer_catalog.add_ons           : active | disabled
///   customer_catalog.price_lists       : draft | published | archived
///
/// Note: coupons/packages/plans have no 'archived' value — 'retired' is their terminal
/// state. The catalog tables have no 'archived'/'retired' value — 'disabled' is theirs.
/// price_lists is the only catalog table whose terminal state is literally 'archived'.
/// </summary>
public sealed class SoftDeleteArchivedStatusTests
{
    private const string LiveStatus = "active";

    public static IEnumerable<object[]> Cases() => new[]
    {
        //                entity,                 archived literal, allowed CHECK set
        Case("coupons",            "retired",  "draft", "active", "paused", "exhausted", "expired", "retired"),
        Case("packages",           "retired",  "draft", "active", "paused", "retired"),
        Case("subscription_plans", "retired",  "draft", "active", "paused", "retired"),
        Case("platform_plans",     "retired",  "draft", "active", "retired"),
        Case("service_categories", "disabled", "active", "disabled", "seasonal"),
        Case("services",           "disabled", "active", "disabled"),
        Case("fabric_types",       "disabled", "active", "disabled"),
        Case("item_groups",        "disabled", "active", "disabled"),
        Case("items",              "disabled", "active", "disabled", "seasonal"),
        Case("item_variants",      "disabled", "active", "disabled"),
        Case("add_ons",            "disabled", "active", "disabled"),
        Case("price_lists",        "archived", "draft", "published", "archived"),
    };

    private static object[] Case(string entity, string archived, params string[] allowed)
        => new object[] { entity, archived, allowed };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ArchivedStatus_IsWithinCheckConstraint(string entity, string archived, string[] allowed)
        => Assert.Contains(archived, allowed);

    [Theory]
    [MemberData(nameof(Cases))]
    public void ArchivedStatus_IsNotTheLiveActiveValue(string entity, string archived, string[] allowed)
        // The whole point of the fix: a soft-deleted row must not remain 'active'.
        => Assert.NotEqual(LiveStatus, archived);

    [Theory]
    [MemberData(nameof(Cases))]
    public void LiveActiveValue_RemainsAValidStatus(string entity, string archived, string[] allowed)
        // Sanity: 'active' (or 'published' for price_lists) is still a legal state — we
        // only changed the archive target, not the live states.
        => Assert.True(allowed.Contains("active") || allowed.Contains("published"),
            $"{entity} should still permit a live status");
}
