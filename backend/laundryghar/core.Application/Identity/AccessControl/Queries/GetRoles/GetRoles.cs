using core.Application.Common.Interfaces;
using core.Application.Identity.Users.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Queries.GetRoles;

public sealed record GetRolesQuery(int Page = 1, int PageSize = 50) : IQuery<IReadOnlyList<RoleDto>>;

public class GetRolesQueryHandler : IQueryHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;
    public GetRolesQueryHandler(ICoreDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<IReadOnlyList<RoleDto>> HandleAsync(GetRolesQuery r, CancellationToken ct)
    {
        // Scope custom roles to the caller's brand (system roles are global) and gate by the brand's
        // vertical — same isolation/vertical rules as the Access-Control console (GetAccessRoles).
        var brandId = _user.TryGetBrandId();
        var brandVertical = brandId is { } bvId
            ? await _db.Brands.AsNoTracking().Where(b => b.Id == bvId).Select(b => b.VerticalKey).FirstOrDefaultAsync(ct)
            : null;

        var page = r.Page < 1 ? 1 : r.Page;
        var size = r.PageSize < 1 ? 50 : r.PageSize;

        var roles = await _db.Roles.AsNoTracking()
            .Where(x => x.BrandId == null || brandId == null || x.BrandId == brandId)
            .OrderBy(x => x.Priority)
            .Select(x => new { x.Id, x.Code, x.Name, x.ScopeType, x.IsSystem, x.Status, x.VerticalKey })
            .ToListAsync(ct);

        return roles
            .Where(x => VerticalKey.IsAvailableTo(x.VerticalKey, brandVertical))
            .Skip((page - 1) * size).Take(size)
            .Select(x => new RoleDto(x.Id, x.Code, x.Name, x.ScopeType, x.IsSystem, x.Status))
            .ToList();
    }
}
