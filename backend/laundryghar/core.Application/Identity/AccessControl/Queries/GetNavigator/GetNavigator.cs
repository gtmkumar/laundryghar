using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Queries.GetNavigator;

public sealed record GetNavigatorQuery : IQuery<NavigatorDto>;

public class GetNavigatorQueryHandler : IQueryHandler<GetNavigatorQuery, NavigatorDto>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;
    public GetNavigatorQueryHandler(ICoreDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<NavigatorDto> HandleAsync(GetNavigatorQuery q, CancellationToken ct)
    {
        var mods = await _db.Modules.AsNoTracking()
            .Where(m => m.ShowInNav && m.Status == "active")
            .OrderBy(m => m.NavOrder)
            .Select(m => new { m.Key, m.Label, m.Icon, m.Route, m.Section, m.RequiredPermission })
            .ToListAsync(ct);

        // Gate each item by the signed-in user's permissions (platform_admin sees all).
        var visible = mods.Where(m =>
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
