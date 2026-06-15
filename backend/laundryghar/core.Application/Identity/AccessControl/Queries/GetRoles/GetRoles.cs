using core.Application.Common.Interfaces;
using core.Application.Identity.Users.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Queries.GetRoles;

public sealed record GetRolesQuery(int Page = 1, int PageSize = 50) : IQuery<IReadOnlyList<RoleDto>>;

public class GetRolesQueryHandler : IQueryHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    private readonly ICoreDbContext _db;
    public GetRolesQueryHandler(ICoreDbContext db) => _db = db;

    public async Task<IReadOnlyList<RoleDto>> HandleAsync(GetRolesQuery r, CancellationToken ct) =>
        await _db.Roles.AsNoTracking().OrderBy(x => x.Priority)
            .Select(x => new RoleDto(x.Id, x.Code, x.Name, x.ScopeType, x.IsSystem, x.Status))
            .ToListAsync(ct);
}
