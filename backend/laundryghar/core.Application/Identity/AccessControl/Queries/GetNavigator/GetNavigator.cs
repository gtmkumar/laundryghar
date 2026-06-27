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
            .OrderBy(m => m.NavOrder)
            .Select(m => new { m.Key, m.Label, m.Icon, m.Route, m.Section, m.RequiredPermission, m.IsCore, m.VerticalKey })
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
        // visible only if it is core OR the active brand has licensed it. Resolved
        // explicitly by brand id so it holds regardless of RLS bypass. With no brand
        // context (e.g. platform admin with no brand selected) entitlement is not
        // applied — they see the full catalogue, still gated by permissions below.
        HashSet<string>? entitled = null;
        if (_config.GetValue<bool>("Entitlement:Enforced")
            && activeBrandId is { } brandId)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            entitled = (await _db.BrandModules.AsNoTracking()
                .Where(bm => bm.BrandId == brandId && bm.Enabled
                          && (bm.ValidUntil == null || bm.ValidUntil >= today))
                .Select(bm => bm.ModuleKey)
                .ToListAsync(ct))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        // Gate each item by vertical, then entitlement (if enforced), then the signed-in
        // user's permissions (platform_admin sees all).
        var visible = mods
            .Where(m => VerticalKey.IsAvailableTo(m.VerticalKey, brandVertical))
            .Where(m => entitled == null || m.IsCore || entitled.Contains(m.Key))
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
