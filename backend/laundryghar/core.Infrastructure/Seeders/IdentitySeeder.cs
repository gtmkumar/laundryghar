using core.Application.Common.Interfaces;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.SharedDataModel.Enums;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace core.Infrastructure.Seeders;

/// <summary>
/// Idempotent bootstrap seeder.
/// C4: Only auto-runs in Development. If --seed is passed in Production, throws to prevent
/// accidental credential seeding in live environments.
/// Admin password is read from Seeder:AdminPassword config (defaults to "Admin@123" in Development).
///
/// Seeds:
/// 1. Permissions catalog (module.action codes)
/// 2. System roles with scope types
/// 3. role_permissions: platform_admin → all; reasonable defaults for others
/// 4. One platform row, one brand row (if none exist)
/// 5. One platform_admin user (admin@laundryghar.local) with platform-scope membership
///
/// Seeding writes bootstrap rows across multiple brands BEFORE any HTTP request has
/// established a tenant context. It must therefore run on a privileged RLS-bypassing
/// (admin/postgres) <see cref="LaundryGharDbContext"/> — see
/// <see cref="laundryghar.SharedDataModel.SeedingSupport.CreatePrivilegedContext"/>.
/// </summary>
public sealed class IdentitySeeder
{
    private readonly LaundryGharDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<IdentitySeeder> _logger;

    public IdentitySeeder(
        LaundryGharDbContext db,
        IPasswordHasher hasher,
        IHostEnvironment env,
        IConfiguration config,
        ILogger<IdentitySeeder> logger)
    {
        _db = db;
        _hasher = hasher;
        _env = env;
        _config = config;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        // C4: Hard guard — seeder must never run unguarded in Production
        if (!_env.IsDevelopment())
        {
            throw new InvalidOperationException(
                "The identity seeder may only run in Development. " +
                "Use a dedicated migration / bootstrap tool for production environments.");
        }

        _logger.LogInformation("Running identity seeders...");

        var permissions = await SeedPermissionsAsync(ct);
        await AssignPermissionModuleKeysAsync(ct);
        var roles = await SeedRolesAsync(ct);
        await SeedRolePermissionsAsync(permissions, roles, ct);
        var (platform, brand) = await SeedOrgHierarchyAsync(ct);
        await SeedAdminUserAsync(platform, brand, roles, ct);

        _logger.LogInformation("Seeding complete.");
    }

    // ─── 1. Permissions ────────────────────────────────────────────────────

    private static readonly (string Code, string Module, string Action, string Name, string Risk)[] PermissionDefs =
    [
        // platform
        ("platforms.list",    "platforms", "list",    "List platforms",    "normal"),
        ("platforms.create",  "platforms", "create",  "Create platform",   "critical"),
        ("platforms.update",  "platforms", "update",  "Update platform",   "high"),
        ("platforms.delete",  "platforms", "delete",  "Delete platform",   "critical"),
        // brands
        ("brands.list",       "brands",    "list",    "List brands",       "low"),
        ("brands.read",       "brands",    "read",    "Read brand",        "low"),
        ("brands.create",     "brands",    "create",  "Create brand",      "high"),
        ("brands.update",     "brands",    "update",  "Update brand",      "high"),
        ("brands.delete",     "brands",    "delete",  "Delete brand",      "critical"),
        // territories
        ("territories.list",  "territories","list",   "List territories",  "low"),
        ("territories.create","territories","create",  "Create territory",  "high"),
        ("territories.update","territories","update",  "Update territory",  "high"),
        ("territories.delete","territories","delete",  "Delete territory",  "high"),
        // franchise_agreements
        ("franchise_agreements.list",  "franchise_agreements","list",   "List agreements", "normal"),
        ("franchise_agreements.create","franchise_agreements","create",  "Create agreement","high"),
        ("franchise_agreements.update","franchise_agreements","update",  "Update agreement","high"),
        ("franchise_agreements.delete","franchise_agreements","delete",  "Delete agreement","critical"),
        // franchises
        ("franchises.list",   "franchises","list",   "List franchises",   "low"),
        ("franchises.read",   "franchises","read",   "Read franchise",    "low"),
        ("franchises.create", "franchises","create", "Create franchise",  "high"),
        ("franchises.update", "franchises","update", "Update franchise",  "high"),
        ("franchises.delete", "franchises","delete", "Delete franchise",  "critical"),
        // stores
        ("stores.list",       "stores","list",     "List stores",       "low"),
        ("stores.read",       "stores","read",     "Read store",        "low"),
        ("stores.create",     "stores","create",   "Create store",      "high"),
        ("stores.update",     "stores","update",   "Update store",      "normal"),
        ("stores.delete",     "stores","delete",   "Delete store",      "high"),
        // warehouses
        ("warehouses.list",   "warehouses","list",  "List warehouses",   "low"),
        ("warehouses.create", "warehouses","create","Create warehouse",  "high"),
        ("warehouses.update", "warehouses","update","Update warehouse",  "high"),
        ("warehouses.delete", "warehouses","delete","Delete warehouse",  "high"),
        // operating_hours
        ("operating_hours.manage","operating_hours","manage","Manage operating hours","normal"),
        // holidays
        ("holidays.manage",   "holidays","manage",  "Manage holidays",   "normal"),
        // store_warehouse_mappings
        ("store_warehouse.manage","store_warehouse","manage","Manage store-warehouse mappings","normal"),
        // users
        ("users.list",           "users","list",           "List users",                    "normal"),
        ("users.read",           "users","read",           "Read user",                     "normal"),
        ("users.read_financial", "users","read_financial", "Read user financial PII (unmasked)", "high"),
        ("users.create",         "users","create",         "Create user",                   "high"),
        ("users.update",         "users","update",         "Update user",                   "normal"),
        ("users.deactivate",     "users","deactivate",     "Deactivate user",               "high"),
        ("users.set_password",   "users","set_password",   "Set user password",             "critical"),
        // roles
        ("roles.list",        "roles","list",     "List roles",        "low"),
        ("roles.manage",      "roles","manage",   "Manage roles",      "critical"),
        ("permissions.list",  "permissions","list","List permissions",  "low"),
        ("permissions.assign","permissions","assign","Assign permissions","critical"),
        ("memberships.grant", "memberships","grant","Grant membership",  "high"),
        ("memberships.revoke","memberships","revoke","Revoke membership","high"),
        // H3: separate permission for changing user_type
        ("users.set_type",    "users","set_type","Set user type","critical"),
        // orders (placeholder)
        ("orders.list",       "orders","list",    "List orders",       "low"),
        ("orders.create",     "orders","create",  "Create order",      "normal"),
        ("orders.update",     "orders","update",  "Update order",      "normal"),
        ("orders.refund",     "orders","refund",  "Refund order",      "high"),
        ("orders.cancel",     "orders","cancel",  "Cancel order",      "normal"),
        // ── BC-3: Catalog & Pricing permissions ──────────────────────────────
        // catalog — service categories, services, fabric types, item groups, items, variants, add-ons
        ("catalog.read",                 "catalog","read",            "Read catalog",                  "low"),
        ("catalog.category.create",      "catalog","category.create", "Create service category",       "high"),
        ("catalog.category.update",      "catalog","category.update", "Update service category",       "normal"),
        ("catalog.category.delete",      "catalog","category.delete", "Delete service category",       "high"),
        ("catalog.service.create",       "catalog","service.create",  "Create service",                "high"),
        ("catalog.service.update",       "catalog","service.update",  "Update service",                "normal"),
        ("catalog.service.delete",       "catalog","service.delete",  "Delete service",                "high"),
        ("catalog.fabric.manage",        "catalog","fabric.manage",   "Manage fabric types",           "normal"),
        ("catalog.itemgroup.manage",     "catalog","itemgroup.manage","Manage item groups",            "normal"),
        ("catalog.item.create",          "catalog","item.create",     "Create item",                   "high"),
        ("catalog.item.update",          "catalog","item.update",     "Update item",                   "normal"),
        ("catalog.item.delete",          "catalog","item.delete",     "Delete item",                   "high"),
        ("catalog.variant.manage",       "catalog","variant.manage",  "Manage item variants",          "normal"),
        ("catalog.addon.manage",         "catalog","addon.manage",    "Manage add-ons",                "normal"),
        // pricing — price lists and price list items
        ("pricing.read",                 "pricing","read",            "Read price lists",              "low"),
        ("pricing.pricelist.create",     "pricing","pricelist.create","Create price list",             "high"),
        ("pricing.pricelist.update",     "pricing","pricelist.update","Update price list",             "normal"),
        ("pricing.pricelist.publish",    "pricing","pricelist.publish","Publish price list",           "high"),
        ("pricing.item.manage",          "pricing","item.manage",     "Manage price list items",       "normal"),
        // customer — admin-side customer management
        ("customer.read",                "customer","read",           "Read customer",                 "normal"),
        ("customer.update",              "customer","update",         "Update customer",               "normal"),
        ("customer.delete",              "customer","delete",         "Delete customer",               "critical"),
        // ── BC-4: Order Lifecycle permissions ────────────────────────────────
        // orders (new; orders.list/create/update/cancel/refund remain from the placeholder set above)
        ("orders.read",                  "orders","read",             "Read orders",                   "low"),
        ("orders.status.update",         "orders","status.update",    "Update order status",           "normal"),
        ("orders.notes.manage",          "orders","notes.manage",     "Manage order notes",            "normal"),
        // pickup / delivery
        ("pickup.read",                  "pickup","read",             "Read pickup requests",          "low"),
        ("pickup.create",                "pickup","create",           "Create pickup request",         "normal"),
        ("pickup.assign",                "pickup","assign",           "Assign rider to pickup",        "high"),
        ("delivery.slot.read",           "delivery","slot.read",      "Read delivery slots",           "low"),
        ("delivery.slot.manage",         "delivery","slot.manage",    "Manage delivery slots",         "normal"),
        ("delivery.assign",              "delivery","assign",         "Assign rider to delivery",      "high"),
        // warehouse / fulfilment units (was garment.* — renamed multi-vertical Phase 1; bridged via PermissionAlias)
        ("fulfillment.read",             "fulfillment","read",        "Read fulfilment records",       "low"),
        ("fulfillment.tag",              "fulfillment","tag",         "Tag/label fulfilment units",    "normal"),
        ("fulfillment.inspect",          "fulfillment","inspect",     "Inspect fulfilment units",      "normal"),
        ("warehouse.batch.manage",       "warehouse","batch.manage",  "Manage warehouse batches",      "normal"),
        ("warehouse.process.scan",       "warehouse","process.scan",  "Scan process log entries",      "normal"),
        ("qc.perform",                   "qc","perform",              "Perform quality checks",        "normal"),
        ("stockrecon.manage",            "stockrecon","manage",       "Manage stock reconciliation",   "high"),
        // ── BC-5: Logistics / Rider permissions ──────────────────────────────
        ("rider.read",                   "rider","read",              "Read rider profiles",           "low"),
        ("rider.manage",                 "rider","manage",            "Manage rider profiles",         "high"),
        ("rider.assignment.read",        "rider","assignment.read",   "Read rider assignments",        "low"),
        ("rider.assignment.manage",      "rider","assignment.manage", "Manage rider assignments",      "normal"),
        ("rider.capacity.manage",        "rider","capacity.manage",   "Manage rider capacity configs", "normal"),
        // Self-scope codes for the rider mobile lane (/api/v1/rider/*). The RiderOnly
        // policy remains the hard boundary; these make the admin permission matrix
        // truthful and let it actually gate what the rider app may do.
        ("rider.tasks.read",             "rider","tasks.read",        "View own assigned pickup/delivery tasks", "low"),
        ("rider.tasks.update",           "rider","tasks.update",      "Progress own assigned tasks (status, OTP, photos, inspection)", "normal"),
        // ── BC-6: Commerce permissions ────────────────────────────────────────
        ("paymentmethod.manage",         "paymentmethod","manage",    "Manage payment methods",        "high"),
        ("packages.manage",              "packages","manage",         "Manage packages",               "normal"),
        ("promotions.manage",            "promotions","manage",       "Manage promotions",             "normal"),
        ("coupons.manage",               "coupons","manage",          "Manage coupons",                "normal"),
        ("loyalty.manage",               "loyalty","manage",          "Manage loyalty programs",       "normal"),
        ("payment.read",                 "payment","read",            "Read payments",                 "low"),
        // R3-SEC-1: payment.record existed only in db/patches/pos_permissions.sql
        ("payment.record",               "payment","record",          "Record offline payment",        "normal"),
        ("payment.refund",               "payment","refund",          "Issue payment refunds",         "high"),
        ("wallet.read",                  "wallet","read",             "Read customer wallets",         "low"),
        ("wallet.adjust",                "wallet","adjust",           "Admin wallet adjustment",       "high"),
        // R3-SEC-1: subscription codes existed only in db/patches/subscriptions_module.sql
        ("subscription.manage",          "subscription","manage",     "Manage subscription plans",     "normal"),
        ("subscription.read",            "subscription","read",       "Read subscription data",        "normal"),
        // R3-SEC-1: saas codes existed only in db/patches/subscriptions_module.sql
        ("saas.manage",                  "saas","manage",             "Manage SaaS plans",             "high"),
        ("saas.read",                    "saas","read",               "Read SaaS subscription data",   "normal"),
        // R3-SEC-2: POS order family — allows POS counter to be locked independently
        ("pos.order.create",             "pos","order.create",        "Create POS order",              "normal"),
        ("pos.order.read",               "pos","order.read",          "Read POS orders",               "low"),
        // ── BC-7: Finance / Royalty permissions ───────────────────────────────
        ("cashbook.read",                "cashbook","read",           "Read cash books",               "low"),
        ("cashbook.manage",              "cashbook","manage",         "Manage cash books",             "high"),
        ("expense.read",                 "expense","read",            "Read expenses",                 "low"),
        ("expense.manage",               "expense","manage",          "Manage expenses",               "normal"),
        ("expense.approve",              "expense","approve",         "Approve/reject expenses",       "high"),
        ("royalty.read",                 "royalty","read",            "Read royalty invoices",         "low"),
        ("royalty.manage",               "royalty","manage",          "Manage royalty invoices",       "high"),
        // ── BC-8: Engagement CMS permissions ─────────────────────────────────
        ("cms.template.manage",          "cms","template.manage",     "Manage notification templates", "normal"),
        ("cms.banner.manage",            "cms","banner.manage",       "Manage app banners",            "normal"),
        ("cms.onboarding.manage",        "cms","onboarding.manage",   "Manage onboarding slides",      "normal"),
        ("cms.appconfig.manage",         "cms","appconfig.manage",    "Manage mobile app config",      "normal"),
        ("cms.notification.read",        "cms","notification.read",   "Read notifications & logs",     "low"),
        ("cms.notification.manage",      "cms","notification.manage", "Manage notifications & retry",  "normal"),
        // ── BC-9: Analytics permissions ───────────────────────────────────────
        ("analytics.read",               "analytics","read",          "Read analytics reports",        "low"),
        ("analytics.refresh",            "analytics","refresh",       "Refresh analytics materialized views","high"),
        // ── customer.create — R3-SEC-1: existed only in db/patches/pos_permissions.sql
        ("customer.create",              "customer","create",         "Create customer (admin)",        "normal"),
        // ── R3-SEC-1: rider.verify + rider.settle existed only in SQL patches
        ("rider.verify",                 "rider","verify",            "Verify rider KYC",              "high"),
        ("rider.settle",                 "rider","settle",            "Settle rider COD cash",         "high"),
        // ── R3-SEC-3: settings permissions — replaces UserType string check in SettingsEndpoints
        ("settings.read",                "settings","read",           "Read admin settings",           "low"),
        ("settings.manage",              "settings","manage",         "Manage admin settings",         "high"),
        // ── RBAC #12: seed-catalog completion ─────────────────────────────────
        // royalty override — §5/§7 canonical critical action
        ("royalty.override",             "royalty","override",        "Override royalty invoice",      "critical"),
        // audit & report modules (§10 audit/report row)
        ("audit.view",                   "audit","view",              "View audit logs",               "low"),
        ("audit.export",                 "audit","export",            "Export audit logs",             "normal"),
        ("report.view",                  "report","view",             "View reports",                  "low"),
        ("report.export",                "report","export",           "Export reports",                "normal"),
        // feature flags (§10 settings/feature_flag row)
        ("feature_flag.view",            "feature_flag","view",       "View feature flags",            "low"),
        ("feature_flag.manage",          "feature_flag","manage",     "Manage feature flags",          "high"),
        // support — code-owned (previously only in db/patches/support_permissions.sql)
        ("support.read",                 "support","read",            "View support inbox",            "low"),
        ("support.manage",               "support","manage",          "Manage support tickets",        "normal"),
        // territories — read (siblings territories.list/create/update/delete already exist)
        ("territories.read",             "territories","read",        "Read territory",                "low"),
        // ── Auditor export set — every *.view/*.list auditor sees, it may also export (§9) ──
        ("orders.export",                "orders","export",           "Export orders",                 "normal"),
        ("payment.export",               "payment","export",          "Export payments",               "normal"),
        ("cashbook.export",              "cashbook","export",         "Export cash books",             "normal"),
        ("expense.export",               "expense","export",          "Export expenses",               "normal"),
        ("royalty.export",               "royalty","export",          "Export royalty invoices",       "normal"),
        ("analytics.export",             "analytics","export",        "Export analytics",              "normal"),
        ("catalog.export",               "catalog","export",          "Export catalog",                "normal"),
        ("pricing.export",               "pricing","export",          "Export price lists",            "normal"),
        ("customer.export",              "customer","export",         "Export customers",              "normal"),
        ("rider.export",                 "rider","export",            "Export riders",                 "normal"),
        ("wallet.export",                "wallet","export",           "Export wallets",                "normal"),
    ];

    private async Task<Dictionary<string, Permission>> SeedPermissionsAsync(CancellationToken ct)
    {
        var existing = await _db.Permissions.ToDictionaryAsync(p => p.Code, ct);
        var now = DateTimeOffset.UtcNow;
        int added = 0;

        foreach (var (code, module, action, name, risk) in PermissionDefs)
        {
            if (existing.ContainsKey(code)) continue;

            var perm = new Permission
            {
                Id = Guid.NewGuid(),
                Code = code,
                Module = module,
                Action = action,
                Name = name,
                RiskLevel = risk,
                IsSystem = true,
                RequiresScope = true,
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Permissions.Add(perm);
            existing[code] = perm;
            added++;
        }

        if (added > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded {Count} permissions.", added);
        }

        return existing;
    }

    // ─── 1b. Canonical owning module for each permission (entitlement) ───────
    // Assigns permissions.module_key for any permission still missing it (newly seeded,
    // or pre-patch rows). Mirrors db/patches/permission_canonical_module.sql so a fresh
    // permission added in code is owned without re-running the patch. Requires the modules
    // catalogue to exist (seeded via SQL); a no-op otherwise (validator surfaces orphans).
    private async Task AssignPermissionModuleKeysAsync(CancellationToken ct)
    {
        var unowned = await _db.Permissions.Where(p => p.ModuleKey == null).ToListAsync(ct);
        if (unowned.Count == 0) return;

        var modules = await _db.Modules.AsNoTracking()
            .Select(m => new { m.Key, m.NavOrder, m.PermissionModules })
            .ToListAsync(ct);
        if (modules.Count == 0) return;

        static bool IsAggregator(string key) => key is "settings" or "dashboard";

        string? OwnerFor(string tag)
        {
            // (a) exact key match wins
            var exact = modules.FirstOrDefault(m => string.Equals(m.Key, tag, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact.Key;
            // (b) module listing the tag — prefer a dedicated module, then lowest nav_order
            return modules
                .Where(m => m.PermissionModules.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(m => IsAggregator(m.Key) ? 1 : 0)
                .ThenBy(m => m.NavOrder)
                .Select(m => (string?)m.Key)
                .FirstOrDefault();
        }

        int assigned = 0;
        foreach (var p in unowned)
        {
            var owner = OwnerFor(p.Module);
            if (owner is not null) { p.ModuleKey = owner; assigned++; }
        }

        if (assigned > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Assigned canonical module_key to {Count} permission(s).", assigned);
        }
    }

    // ─── 2. Roles ──────────────────────────────────────────────────────────

    // VerticalKey: null = vertical-neutral (every brand); a value gates the role to that vertical
    // (mirrors modules — see GetAccessRoles / GetNavigator). The on-site processing scope
    // (ScopeType.Warehouse) is the only vertical-specific tier: laundry has warehouse_*, salon has a
    // salon manager/stylist, logistics has a hub supervisor/operator — all sharing the same
    // operational permission set (only the labels differ per vertical).
    private static readonly (string Code, string Name, string ScopeType, short Priority, string? VerticalKey)[] RoleDefs =
    [
        ("platform_admin",       "Platform Administrator",  ScopeType.Platform,  10,  null),
        ("brand_admin",          "Brand Administrator",     ScopeType.Brand,     20,  null),
        // Priority 24: brand-level oversight that outranks the focused brand managers
        // (operations_manager=25, finance=26, …) but stays below brand_admin (20).
        ("regional_manager",     "Regional Manager",        ScopeType.Brand,     24,  null),
        ("franchise_owner",      "Franchise Owner",         ScopeType.Franchise, 40,  null),
        ("store_admin",          "Store Administrator",     ScopeType.Store,     50,  null),
        ("store_staff",          "Store Staff",             ScopeType.Store,     60,  null),
        // Laundry on-site processing (warehouse).
        ("warehouse_supervisor", "Warehouse Supervisor",    ScopeType.Warehouse, 70,  VerticalKey.Laundry),
        ("warehouse_staff",      "Warehouse Staff",         ScopeType.Warehouse, 80,  VerticalKey.Laundry),
        // Salon on-site service (studio).
        ("salon_manager",        "Salon Manager",           ScopeType.Warehouse, 70,  VerticalKey.Salon),
        ("salon_staff",          "Stylist",                 ScopeType.Warehouse, 80,  VerticalKey.Salon),
        // Logistics on-site (hub).
        ("hub_supervisor",       "Hub Supervisor",          ScopeType.Warehouse, 70,  VerticalKey.Logistics),
        ("hub_operator",         "Hub Operator",            ScopeType.Warehouse, 80,  VerticalKey.Logistics),
        ("rider",                "Rider",                   ScopeType.Store,     90,  null),
        ("auditor",              "Auditor",                 ScopeType.Platform,  100, null),
        // RBAC #12: brand-scoped customer support. NOT the RaaS partner_* roles — those
        // need a logistics_partner scope_type absent from the CHECK/ScopeType enum (out of scope).
        ("support",              "Support",                 ScopeType.Brand,     110, null),
    ];

    private async Task<Dictionary<string, Role>> SeedRolesAsync(CancellationToken ct)
    {
        var existing = await _db.Roles.IgnoreQueryFilters()
            .Where(r => r.IsSystem && r.BrandId == null)
            .ToDictionaryAsync(r => r.Code, ct);
        var now = DateTimeOffset.UtcNow;
        int added = 0;

        foreach (var (code, name, scopeType, priority, verticalKey) in RoleDefs)
        {
            if (existing.ContainsKey(code)) continue;
            var role = new Role
            {
                Id = Guid.NewGuid(),
                BrandId = null,
                Code = code,
                Name = name,
                ScopeType = scopeType,
                VerticalKey = verticalKey,
                IsSystem = true,
                IsAssignable = true,
                Priority = priority,
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Roles.Add(role);
            existing[code] = role;
            added++;
        }

        if (added > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded {Count} roles.", added);
        }

        return existing;
    }

    // ─── 3. Role Permissions ───────────────────────────────────────────────

    private async Task SeedRolePermissionsAsync(
        Dictionary<string, Permission> permissions,
        Dictionary<string, Role> roles,
        CancellationToken ct)
    {
        // Load existing mappings
        var existing = await _db.RolePermissions
            .Select(rp => new { rp.RoleId, rp.PermissionId })
            .ToListAsync(ct);
        var existingSet = existing.Select(x => (x.RoleId, x.PermissionId)).ToHashSet();

        var now = DateTimeOffset.UtcNow;
        var toAdd = new List<RolePermission>();

        void Grant(string roleCode, IEnumerable<string> permCodes)
        {
            if (!roles.TryGetValue(roleCode, out var role)) return;
            foreach (var code in permCodes)
            {
                if (!permissions.TryGetValue(code, out var perm)) continue;
                if (existingSet.Contains((role.Id, perm.Id))) continue;
                toAdd.Add(new RolePermission
                {
                    Id = Guid.NewGuid(),
                    RoleId = role.Id,
                    PermissionId = perm.Id,
                    GrantedAt = now,
                    CreatedAt = now
                });
                existingSet.Add((role.Id, perm.Id));
            }
        }

        // platform_admin → all permissions
        Grant("platform_admin", permissions.Keys);

        // brand_admin
        Grant("brand_admin", [
            "brands.list","brands.read","brands.update",
            "franchises.list","franchises.read","franchises.create","franchises.update","franchises.delete",
            "territories.list","territories.create","territories.update",
            "stores.list","stores.read","stores.create","stores.update","stores.delete",
            "warehouses.list","warehouses.create","warehouses.update","warehouses.delete",
            "users.list","users.read","users.read_financial","users.create","users.update","users.deactivate","users.set_type",
            "roles.list","roles.manage","permissions.list","permissions.assign","memberships.grant","memberships.revoke",
            "operating_hours.manage","holidays.manage","store_warehouse.manage",
            "orders.list","orders.update","orders.cancel","orders.refund",
            // BC-3: catalog + pricing (full) + customer admin
            "catalog.read","catalog.category.create","catalog.category.update","catalog.category.delete",
            "catalog.service.create","catalog.service.update","catalog.service.delete",
            "catalog.fabric.manage","catalog.itemgroup.manage",
            "catalog.item.create","catalog.item.update","catalog.item.delete",
            "catalog.variant.manage","catalog.addon.manage",
            "pricing.read","pricing.pricelist.create","pricing.pricelist.update","pricing.pricelist.publish",
            "pricing.item.manage",
            "customer.read","customer.update","customer.delete",
            // BC-4: order lifecycle + pickup/delivery + warehouse/garments
            "orders.read","orders.status.update","orders.notes.manage",
            "pickup.read","pickup.create","pickup.assign",
            "delivery.slot.read","delivery.slot.manage","delivery.assign",
            "fulfillment.read","fulfillment.tag","fulfillment.inspect",
            "warehouse.batch.manage","warehouse.process.scan","qc.perform","stockrecon.manage",
            // BC-5: logistics — brand_admin gets all rider permissions (R3-SEC-1: verify + settle folded in)
            "rider.read","rider.manage","rider.assignment.read","rider.assignment.manage","rider.capacity.manage",
            "rider.verify","rider.settle",
            // BC-6: commerce — brand_admin gets all commerce permissions (R3-SEC-1: record + customer.create folded in)
            "paymentmethod.manage","packages.manage","promotions.manage","coupons.manage","loyalty.manage",
            "payment.read","payment.record","payment.refund","wallet.read","wallet.adjust",
            // BC-6: subscriptions (R3-SEC-1: subscription.read/manage folded in)
            "subscription.manage","subscription.read",
            // BC-7: finance — brand_admin gets all finance permissions
            "cashbook.read","cashbook.manage",
            "expense.read","expense.manage","expense.approve",
            "royalty.read","royalty.manage",
            // BC-8: engagement CMS — brand_admin gets all CMS permissions
            "cms.template.manage","cms.banner.manage","cms.onboarding.manage",
            "cms.appconfig.manage","cms.notification.read","cms.notification.manage",
            // BC-9: analytics — brand_admin gets read + refresh
            "analytics.read","analytics.refresh",
            // customer.create — counter-side customer creation (R3-SEC-1)
            "customer.create",
            // POS — brand_admin can do POS operations (R3-SEC-2)
            "pos.order.create","pos.order.read",
            // Settings — brand_admin manages settings (R3-SEC-3)
            "settings.read","settings.manage",
            // RBAC #12: feature flags (§10 settings/feature_flag → brand_admin M)
            "feature_flag.view","feature_flag.manage",
            // RBAC #12: audit & report (§10 audit/report → brand_admin M)
            "audit.view","audit.export","report.view","report.export",
            // RBAC #12: data export set (brand_admin is enumerated; mirrors the auditor export set)
            "orders.export","payment.export","cashbook.export","expense.export","royalty.export",
            "analytics.export","catalog.export","pricing.export","customer.export","rider.export","wallet.export",
        ]);

        // franchise_owner
        Grant("franchise_owner", [
            "franchises.read","franchises.update",
            "stores.list","stores.read","stores.create","stores.update",
            "warehouses.list","warehouses.create","warehouses.update",
            "users.list","users.read","users.create","users.update","users.deactivate",
            "orders.list","orders.update","orders.cancel",
            "operating_hours.manage","holidays.manage",
            // BC-3: read-only catalog + pricing + customer
            "catalog.read","pricing.read","customer.read",
            // BC-4: read-only visibility across order lifecycle
            "orders.read","pickup.read","delivery.slot.read","fulfillment.read",
            // BC-5: logistics — franchise_owner gets *.read (R3-SEC-1: verify + settle + manage added per sql patches)
            "rider.read","rider.assignment.read","rider.manage","rider.verify","rider.settle",
            // BC-6: commerce — franchise_owner gets *.read (R3-SEC-1: payment.record + customer.create added per sql patches)
            "payment.read","payment.record","wallet.read","customer.create",
            // BC-6: subscriptions — franchise_owner gets subscription.read/manage (R3-SEC-1)
            "subscription.manage","subscription.read",
            // BC-7: finance — franchise_owner: cashbook.read, expense.read, expense.approve, royalty.read
            "cashbook.read","expense.read","expense.approve","royalty.read",
            // BC-9: analytics — franchise_owner gets read only
            "analytics.read",
            // POS — franchise_owner can use POS (R3-SEC-2)
            "pos.order.create","pos.order.read",
            // RBAC #12: feature flags — franchise_owner view (§10 settings/feature_flag → V)
            "feature_flag.view",
        ]);

        // store_admin
        Grant("store_admin", [
            "stores.read","stores.update",
            "users.list","users.read","users.create","users.update","users.deactivate",
            "orders.list","orders.create","orders.update","orders.cancel",
            "operating_hours.manage","holidays.manage",
            // BC-3: read-only catalog + pricing
            "catalog.read","pricing.read","customer.read",
            // BC-4: full orders + pickup + delivery slot + garment ops
            "orders.read","orders.status.update","orders.notes.manage",
            "pickup.read","pickup.create","pickup.assign",
            "delivery.slot.read","delivery.slot.manage",
            "fulfillment.read","fulfillment.tag","fulfillment.inspect",
            // BC-5: logistics — store_admin gets rider.read + assignment read/manage
            "rider.read","rider.assignment.read","rider.assignment.manage",
            // BC-6: commerce — store_admin gets payment.read/record, wallet.read, coupons.manage
            // (R3-SEC-1: payment.record + customer.create added per pos_permissions.sql)
            "payment.read","payment.record","wallet.read","coupons.manage","customer.create",
            // BC-7: finance — store_admin: cashbook.manage, expense.manage
            "cashbook.manage","expense.manage",
            // BC-8: engagement CMS — store_admin gets banner management only
            "cms.banner.manage",
            // POS — store_admin is the primary POS operator (R3-SEC-2)
            "pos.order.create","pos.order.read",
            // RBAC #12: feature flags — store_admin view (§10 settings/feature_flag → V(store))
            "feature_flag.view",
        ]);

        // store_staff
        Grant("store_staff", [
            "stores.read","orders.list","orders.create","orders.update",
            // BC-3: read-only catalog + pricing
            "catalog.read","pricing.read",
            // BC-4: orders + pickup + delivery slot read + garment ops
            "orders.read","orders.status.update","orders.notes.manage",
            "pickup.read","pickup.create",
            "delivery.slot.read",
            "fulfillment.read","fulfillment.tag","fulfillment.inspect",
            // R3-SEC-2: POS — store_staff is the primary counter user; grant POS family
            "pos.order.create","pos.order.read",
            // R3-SEC-1: payment.record + customer.create for POS counter workflow
            "payment.record","customer.create",
        ]);

        // On-site processing/service roles share one permission set per tier across verticals
        // (laundry warehouse_*, salon salon_manager/staff, logistics hub_*). The codes are
        // operational, not laundry-specific; per-vertical nav/entitlement gating already hides the
        // modules a vertical doesn't license, so over-granting across verticals is inert.
        string[] onsiteSupervisorPerms =
        [
            "warehouses.list","orders.list","orders.update",
            // BC-4: fulfillment.*, warehouse.*, qc, stockrecon, orders.read
            "orders.read",
            "fulfillment.read","fulfillment.tag","fulfillment.inspect",
            "warehouse.batch.manage","warehouse.process.scan",
            "qc.perform","stockrecon.manage",
            // BC-5: logistics — supervisor gets rider.read + assignment.manage + capacity.manage
            "rider.read","rider.assignment.manage","rider.capacity.manage",
        ];
        string[] onsiteStaffPerms =
        [
            "orders.list",
            // BC-4: on-site item ops + process scan + orders.read
            "orders.read","fulfillment.read","fulfillment.tag","fulfillment.inspect",
            "warehouse.process.scan",
        ];

        // laundry / salon / logistics on-site supervisor + staff
        Grant("warehouse_supervisor", onsiteSupervisorPerms);
        Grant("salon_manager", onsiteSupervisorPerms);
        Grant("hub_supervisor", onsiteSupervisorPerms);
        Grant("warehouse_staff", onsiteStaffPerms);
        Grant("salon_staff", onsiteStaffPerms);
        Grant("hub_operator", onsiteStaffPerms);

        // rider — self-scope task permissions only. Riders never touch admin order
        // APIs; their order mutations flow through /api/v1/rider/* which is gated
        // by RiderOnly + these codes (least privilege: no orders.* grants).
        Grant("rider", [
            "rider.tasks.read","rider.tasks.update",
        ]);

        // regional_manager — brand-scoped operational oversight across the brand's
        // stores: full order/logistics/warehouse management, read-only finance &
        // analytics, and team visibility. No destructive admin (no user/role grants,
        // no pricing edits, no finance writes). Previously shipped with ZERO
        // permissions, leaving anyone assigned the role able to see only the
        // ungated Dashboard + CMS.
        Grant("regional_manager", [
            // Org visibility
            "stores.list","stores.read","warehouses.list","franchises.list","franchises.read",
            // Orders — full lifecycle
            "orders.list","orders.read","orders.update","orders.cancel",
            "orders.status.update","orders.notes.manage",
            // Pickup / delivery
            "pickup.read","pickup.create","pickup.assign",
            "delivery.slot.read","delivery.slot.manage","delivery.assign",
            // Warehouse / garments
            "fulfillment.read","fulfillment.tag","fulfillment.inspect",
            "warehouse.batch.manage","warehouse.process.scan","qc.perform","stockrecon.manage",
            // Riders / logistics
            "rider.read","rider.manage","rider.assignment.read","rider.assignment.manage","rider.capacity.manage",
            // Customers + catalog/pricing read
            "customer.read","customer.update","catalog.read","pricing.read",
            // POS read
            "pos.order.read",
            // Finance + analytics — read only
            "cashbook.read","expense.read","royalty.read","analytics.read",
            // Team visibility
            "users.list","roles.list",
        ]);

        // auditor
        Grant("auditor", [
            "brands.list","brands.read","franchises.list","franchises.read",
            "stores.list","stores.read","warehouses.list","orders.list",
            "users.list","roles.list","permissions.list",
            // BC-3: read-only catalog + pricing + customer
            "catalog.read","pricing.read","customer.read",
            // BC-4: all *.read across order lifecycle
            "orders.read","pickup.read","delivery.slot.read","fulfillment.read",
            // BC-5: logistics — auditor gets *.read
            "rider.read","rider.assignment.read",
            // BC-6: commerce — auditor gets *.read
            "payment.read","wallet.read",
            // BC-7: finance — auditor gets all *.read
            "cashbook.read","expense.read","royalty.read",
            // BC-8: engagement CMS — auditor gets notification read
            "cms.notification.read",
            // BC-9: analytics — auditor gets read only
            "analytics.read",
            // RBAC #12: audit & report (§10 audit/report → auditor M = view + export)
            "audit.view","audit.export","report.view","report.export",
            // RBAC #12: §9 auditors get *.view + *.export across their scope
            "orders.export","payment.export","cashbook.export","expense.export","royalty.export",
            "analytics.export","catalog.export","pricing.export","customer.export","rider.export","wallet.export",
        ]);

        // support — brand-scoped customer support (§10 support column). Read-only across the
        // order lifecycle + customer + subscription, order notes, and refund within cap; plus
        // the support inbox itself. No create/update/delete of catalog/pricing/users.
        // (partner_booking view is omitted: those permissions belong to the RaaS workstream and
        //  do not exist yet — Grant() would no-op them anyway.)
        Grant("support", [
            // order view + notes (§10 order → V+notes)
            "orders.list","orders.read","orders.notes.manage",
            // customer read
            "customer.read",
            // pickup / delivery view (§10 pickup/delivery → V)
            "pickup.read","delivery.slot.read",
            // subscription view (§10 subscription → V)
            "subscription.read",
            // refund within cap (§10 payment/refund → X(refund cap)); the cap is enforced at the endpoint
            "payment.refund",
            // support inbox
            "support.read","support.manage",
        ]);

        // ─── §9 belt-and-braces: deny rows (first-ever effect='deny' seeds) ─────
        // ScopeResolver already honours deny-wins. Idempotent via the SAME existingSet dedup
        // as Grant(), so re-runs never violate UNIQUE(role_id, permission_id).
        void Deny(string roleCode, IEnumerable<string> permCodes)
        {
            if (!roles.TryGetValue(roleCode, out var role)) return;
            foreach (var code in permCodes)
            {
                if (!permissions.TryGetValue(code, out var perm)) continue;
                if (existingSet.Contains((role.Id, perm.Id))) continue;
                toAdd.Add(new RolePermission
                {
                    Id = Guid.NewGuid(),
                    RoleId = role.Id,
                    PermissionId = perm.Id,
                    Effect = "deny",
                    GrantedAt = now,
                    CreatedAt = now
                });
                existingSet.Add((role.Id, perm.Id));
            }
        }

        // The closed set of mutating verbs. A permission whose action segment (the last
        // dot-separated token of its code) is one of these is a write. Kept in lockstep with
        // db/patches/rbac_deny_rows.sql.
        var mutatingVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "create","update","delete","manage","approve","publish","assign","cancel",
            "refund","override","record","adjust","settle","verify","perform","scan",
            "tag","inspect","reconcile","close","generate","issue","void","reject",
            "activate","deactivate"
        };
        var mutatingCodes = PermissionDefs
            .Where(d => mutatingVerbs.Contains(d.Action.Split('.').Last()))
            .Select(d => d.Code);

        // auditor is read-only (§9): deny every mutating permission. The Grant() calls above
        // already added its *.view/*.list/*.read/*.export allows to existingSet, so Deny() skips
        // those pairs — leaving exactly (mutating − auditor-allowed) denied.
        Deny("auditor", mutatingCodes);

        // franchise_owner: everything except royalty.override (§7 canonical deny example). The
        // role is never granted royalty.override, so this deny only bites when a user also holds
        // a higher role that grants it — deny still wins.
        Deny("franchise_owner", ["royalty.override"]);

        if (toAdd.Count > 0)
        {
            _db.RolePermissions.AddRange(toAdd);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded {Count} role-permission assignments.", toAdd.Count);
        }
    }

    // ─── 4. Org hierarchy ─────────────────────────────────────────────────

    private async Task<(Platform platform, Brand brand)> SeedOrgHierarchyAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Platform
        var platform = await _db.Platforms.IgnoreQueryFilters().FirstOrDefaultAsync(ct);
        if (platform is null)
        {
            platform = new Platform
            {
                Id = Guid.NewGuid(),
                Code = "LG",
                Name = "Laundry Ghar",
                LegalName = "Laundry Ghar Pvt Ltd",
                Domain = "laundryghar.com",
                Config = "{}",
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1
            };
            _db.Platforms.Add(platform);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded platform {PlatformId}.", platform.Id);
        }

        // Brand
        var brand = await _db.Brands.IgnoreQueryFilters()
            .Where(b => b.PlatformId == platform.Id)
            .FirstOrDefaultAsync(ct);

        if (brand is null)
        {
            brand = new Brand
            {
                Id = Guid.NewGuid(),
                PlatformId = platform.Id,
                Code = "LG-MAIN",
                Name = "Laundry Ghar",
                LegalName = "Laundry Ghar Pvt Ltd",
                CurrencyCode = "INR",
                CountryCode = "IN",
                Timezone = "Asia/Kolkata",
                LocaleDefault = "en-IN",
                LocalesEnabled = ["en-IN", "hi-IN"],
                Config = "{}",
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1
            };
            _db.Brands.Add(brand);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded brand {BrandId}.", brand.Id);
        }

        return (platform, brand);
    }

    // ─── 5. Admin user ────────────────────────────────────────────────────

    private async Task SeedAdminUserAsync(
        Platform platform,
        Brand brand,
        Dictionary<string, Role> roles,
        CancellationToken ct)
    {
        const string AdminEmail = "admin@laundryghar.local";
        const string AdminPhone = "+919999999999";

        // C4: Admin password read from config; fallback to "Admin@123" only in Development.
        var adminPassword = _config["Seeder:AdminPassword"] ?? "Admin@123";

        var now = DateTimeOffset.UtcNow;

        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == AdminEmail, ct);

        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                Email = AdminEmail,
                PhoneE164 = AdminPhone,
                PasswordHash = _hasher.Hash(adminPassword),
                UserType = UserType.PlatformAdmin,
                Status = UserStatus.Active,
                EmailVerifiedAt = now,
                PhoneVerifiedAt = now,
                Locale = "en-IN",
                Timezone = "Asia/Kolkata",
                FailedAttempts = 0,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);

            _db.UserProfiles.Add(new UserProfile
            {
                UserId = user.Id,
                FirstName = "Platform",
                LastName = "Admin",
                DisplayName = "Platform Admin",
                Preferences = "{}",
                Metadata = "{}",
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now
            });
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded admin user {UserId} ({Email}).", user.Id, AdminEmail);
        }

        // Ensure platform-scope membership to platform_admin role
        if (roles.TryGetValue("platform_admin", out var platformAdminRole))
        {
            var hasMembership = await _db.UserScopeMemberships
                .AnyAsync(m => m.UserId == user.Id
                            && m.ScopeType == ScopeType.Platform
                            && m.RoleId == platformAdminRole.Id
                            && m.RevokedAt == null, ct);

            if (!hasMembership)
            {
                _db.UserScopeMemberships.Add(new UserScopeMembership
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    ScopeType = ScopeType.Platform,
                    ScopeId = null,
                    RoleId = platformAdminRole.Id,
                    IsPrimary = true,
                    GrantedAt = now,
                    Metadata = "{}",
                    CreatedAt = now
                });
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Granted platform_admin membership to admin user.");
            }
        }
    }
}
