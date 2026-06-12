using System.Reflection;
using System.Text.RegularExpressions;
using laundryghar.Identity.Infrastructure.Seeders;

namespace laundryghar.Identity.Tests.Auth;

/// <summary>
/// Drift gate: asserts that every permission code referenced via
/// RequireAuthorization("permission:...") in endpoint code is also present in
/// IdentitySeeder.PermissionDefs, so fresh-bootstrap environments always have
/// the codes they need.
///
/// How it works:
///   1. Reads the PermissionDefs private static field via reflection.
///   2. Compares against a hardcoded set of codes that represent the full
///      union observed by grepping  permission:  strings across backend/**/*.cs
///      (excluding comments and PermissionPolicyProvider constants).
///   3. Any code in the endpoint set but not in PermissionDefs → test FAILS,
///      alerting you that the seeder is drifting from the live permission model.
///
/// Maintenance: when you add a new RequireAuthorization("permission:X") call,
/// add X to EndpointPermissionCodes below.
/// </summary>
public sealed class PermissionDriftTests
{
    /// <summary>
    /// Complete set of permission codes enforced by RequireAuthorization() calls
    /// across all services. Pipe-separated codes (AnyPermission policies) are
    /// expanded to individual entries.
    ///
    /// Source: grep 'RequireAuthorization("permission:' backend/**/*.cs
    ///         (updated as of 2026-06-12, R3-SEC-1/2/3 changes included).
    /// </summary>
    private static readonly HashSet<string> EndpointPermissionCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Analytics
        "analytics.read",
        "analytics.refresh",
        // Catalog — categories, services, fabrics, item groups, items, variants, addons
        "catalog.read",
        "catalog.category.create",
        "catalog.category.update",
        "catalog.category.delete",
        "catalog.service.create",
        "catalog.service.update",
        "catalog.service.delete",
        "catalog.fabric.manage",
        "catalog.itemgroup.manage",
        "catalog.item.create",
        "catalog.item.update",
        "catalog.item.delete",
        "catalog.variant.manage",
        "catalog.addon.manage",
        // Catalog — customers
        "customer.read",
        "customer.update",
        "customer.delete",
        "customer.create",       // R3-SEC-1: was patch-only
        // Catalog — pricing
        "pricing.read",
        "pricing.pricelist.create",
        "pricing.pricelist.update",
        "pricing.pricelist.publish",
        "pricing.item.manage",
        // Commerce — payment methods, packages, promotions, coupons, loyalty
        "paymentmethod.manage",
        "packages.manage",
        "promotions.manage",
        "coupons.manage",
        "loyalty.manage",
        // Commerce — payments
        "payment.read",
        "payment.record",        // R3-SEC-1: was patch-only
        "payment.refund",
        // Commerce — wallet
        "wallet.read",
        "wallet.adjust",
        // Commerce — subscriptions (R3-SEC-1: were patch-only)
        "subscription.manage",
        "subscription.read",
        // Finance
        "cashbook.read",
        "cashbook.manage",
        "expense.read",
        "expense.manage",
        "expense.approve",
        "royalty.read",
        "royalty.manage",
        // CMS / Engagement
        "cms.template.manage",
        "cms.banner.manage",
        "cms.onboarding.manage",
        "cms.appconfig.manage",
        "cms.notification.read",
        "cms.notification.manage",
        // Identity — users, roles, memberships
        "users.read",
        "users.create",
        "users.update",
        "users.deactivate",
        "users.set_password",
        "users.set_type",
        "roles.list",
        "permissions.list",
        "permissions.assign",
        "memberships.grant",
        // Identity — settings (R3-SEC-3)
        "settings.read",
        "settings.manage",
        // Logistics — riders
        "rider.read",
        "rider.manage",
        "rider.verify",          // R3-SEC-1: was patch-only
        "rider.settle",          // R3-SEC-1: was patch-only
        "rider.assignment.read",
        "rider.assignment.manage",
        "rider.capacity.manage",
        "rider.tasks.read",
        "rider.tasks.update",
        // Orders (admin)
        "orders.list",
        "orders.read",
        "orders.create",
        "orders.status.update",
        "orders.cancel",
        "orders.notes.manage",
        "orders.update",
        // Orders (invoice)
        "orders.read",           // duplicate — set deduplicates automatically
        "orders.update",
        // POS order family (R3-SEC-2)
        "pos.order.create",
        "pos.order.read",
        // Pickup / delivery
        "pickup.read",
        "pickup.create",
        "pickup.assign",
        "delivery.slot.read",
        "delivery.slot.manage",
        "delivery.assign",
        // Warehouse / garments
        "garment.read",
        "garment.tag",
        "garment.inspect",
        "warehouse.batch.manage",
        "warehouse.process.scan",
        "qc.perform",
        "stockrecon.manage",
    };

    [Fact]
    public void AllEndpointPermissionCodes_ExistInPermissionDefs()
    {
        // Access the private static PermissionDefs via reflection.
        var field = typeof(IdentitySeeder).GetField(
            "PermissionDefs",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field); // guard: if the field is renamed, this test breaks loudly

        // The field is a ValueTuple array: (string Code, string Module, string Action, string Name, string Risk)[]
        // We only care about Code (index 0).
        var raw = field.GetValue(null)!;
        var seededCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Use reflection to iterate the array of value tuples
        var array = (System.Collections.IEnumerable)raw;
        foreach (var item in array)
        {
            // Each element is a ValueTuple<string,string,string,string,string>
            var itemType = item.GetType();
            var codeField = itemType.GetField("Item1");
            if (codeField?.GetValue(item) is string code)
                seededCodes.Add(code);
        }

        // Find codes that endpoints enforce but the seeder does not define
        var missing = EndpointPermissionCodes.Except(seededCodes, StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList();

        Assert.True(
            missing.Count == 0,
            $"The following permission codes are enforced by endpoints but are missing from " +
            $"IdentitySeeder.PermissionDefs. Fresh-bootstrap environments will be broken. " +
            $"Add them to PermissionDefs:\n  {string.Join("\n  ", missing)}");
    }

    [Fact]
    public void PermissionDefs_HasNoDuplicateCodes()
    {
        var field = typeof(IdentitySeeder).GetField(
            "PermissionDefs",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field);

        var raw = field.GetValue(null)!;
        var codes = new List<string>();

        var array = (System.Collections.IEnumerable)raw;
        foreach (var item in array)
        {
            var codeField = item.GetType().GetField("Item1");
            if (codeField?.GetValue(item) is string code)
                codes.Add(code);
        }

        var duplicates = codes
            .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .OrderBy(c => c)
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            $"PermissionDefs contains duplicate codes:\n  {string.Join("\n  ", duplicates)}");
    }
}
