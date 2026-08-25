using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace core.Application.Identity.AccessControl.Queries.GetNavigator;

public sealed record GetNavigatorQuery : IQuery<NavigatorDto>;

public class GetNavigatorQueryHandler : IQueryHandler<GetNavigatorQuery, NavigatorDto>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IConfiguration _config;
    public GetNavigatorQueryHandler(ICoreDbContext db, ICurrentUser user, IConfiguration config)
    {
        _db = db; _user = user; _config = config;
    }

    public async Task<NavigatorDto> HandleAsync(GetNavigatorQuery q, CancellationToken ct)
    {
        var mods = await _db.Modules.AsNoTracking()
            .Where(m => m.ShowInNav && m.Status == "active")
            // ThenBy(Key) is not decoration. Ordering by NavOrder alone left three pairs of rows
            // sharing a value (cms/promotions, fabrics/subscriptions, appointments/warehouse), and
            // an unordered tie resolves to whatever Postgres returns — the live DB and the browser
            // disagreed about CMS vs Promotions, and both were "correct". Migration 0022 removed
            // those ties and forbids new ones; this makes the NEXT tie degrade to alphabetical
            // rather than to chance, so the menu can never reshuffle itself between page loads.
            .OrderBy(m => m.NavOrder).ThenBy(m => m.Key)
            .Select(m => new { m.Key, m.Label, m.Icon, m.Route, m.Section, m.RequiredPermission, m.IsCore, m.VerticalKey, m.FeatureKey })
            .ToListAsync(ct);

        var activeBrandId = _user.TryGetBrandId();

        // Vertical gate: a module tagged with a vertical_key (e.g. laundry fabric management) is
        // shown only to brands of that vertical. A neutral (null) module shows to all; with no
        // brand context (platform admin) every module passes. (Multi-vertical Phase 2.)
        string? brandVertical = null;
        if (activeBrandId is { } vbId)
            brandVertical = await _db.Brands.AsNoTracking()
                .Where(x => x.Id == vbId).Select(x => x.VerticalKey).FirstOrDefaultAsync(ct);

        // PaaS entitlement gate (Phase 2, behind a flag): when enforced, a module is
        // visible only if it is core OR the active brand has licensed the FEATURE behind it.
        // Resolved explicitly by brand id so it holds regardless of RLS bypass. With no brand
        // context (e.g. platform admin with no brand selected) entitlement is not
        // applied — they see the full catalogue, still gated by permissions below.
        //
        // Keyed on features rather than modules since migration 0005: a brand buys features, and
        // a module appears when its feature is owned. Core features are always on, mirroring the
        // token-side filter in ScopeResolver so the sidebar and the API can never disagree.
        HashSet<string>? entitledFeatures = null;
        if (_config.GetValue<bool>("Entitlement:Enforced")
            && activeBrandId is { } brandId)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            entitledFeatures = (await _db.Features.AsNoTracking()
                .Where(f => f.Status == "active" && (f.IsCore ||
                    _db.BrandFeatures.Any(bf => bf.BrandId == brandId && bf.FeatureKey == f.Key
                        && bf.Enabled && (bf.ValidUntil == null || bf.ValidUntil >= today))))
                .Select(f => f.Key)
                .ToListAsync(ct))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        // Gate each item by vertical, then entitlement (if enforced), then the signed-in
        // user's permissions (platform_admin sees all).
        var visible = mods
            .Where(m => VerticalKey.IsAvailableTo(m.VerticalKey, brandVertical))
            .Where(m => entitledFeatures == null
                     || m.IsCore
                     || (m.FeatureKey is not null && entitledFeatures.Contains(m.FeatureKey)))
            .Where(m =>
                string.IsNullOrEmpty(m.RequiredPermission)
                || _user.IsPlatformAdmin
                || _user.HasPermission(m.RequiredPermission));

        var sections = visible
            .GroupBy(m => m.Section ?? "General")
            .Select(g => new NavSectionDto(g.Key,
                g.Select(m => new NavItemDto(m.Key, m.Label, m.Icon, m.Route)).ToList()))
            .ToList();

        return new NavigatorDto(sections);
    }
}
