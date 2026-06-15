using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Queries.GetAccessRoles;

public sealed record GetAccessRolesQuery : IQuery<AccessRolesDto>;

public class GetAccessRolesQueryHandler : IQueryHandler<GetAccessRolesQuery, AccessRolesDto>
{
    private readonly ICoreDbContext _db;
    public GetAccessRolesQueryHandler(ICoreDbContext db) => _db = db;

    public async Task<AccessRolesDto> HandleAsync(GetAccessRolesQuery q, CancellationToken ct)
    {
        var matrix = await ModuleMatrix.LoadAsync(_db, ct);

        var roles = await _db.Roles.AsNoTracking()
            .Where(r => r.DeletedAt == null && r.Status == "active")
            .Select(r => new
            {
                r.Id,
                r.Code,
                r.Name,
                r.Description,
                r.ScopeType,
                r.IsSystem,
                r.Priority,
                PermCodes = r.RolePermissions.Select(rp => rp.Permission.Code).ToList(),
                MemberCount = r.UserScopeMemberships.Count(m => m.RevokedAt == null),
            })
            .ToListAsync(ct);

        var summaries = roles.Select(r =>
        {
            var onCells = new HashSet<string>();
            foreach (var code in r.PermCodes)
                foreach (var cell in matrix.CellsFor(PermissionMatrix.Module(code), PermissionMatrix.Action(code)))
                    onCells.Add(cell);
            return new
            {
                r.ScopeType,
                Dto = new RoleSummaryDto(r.Id, r.Code, r.Name, r.Description, r.ScopeType, r.IsSystem,
                    r.MemberCount, onCells.OrderBy(c => c).ToList()),
                r.Priority,
            };
        }).ToList();

        RoleGroupDto Group(string tier, string label) =>
            new(tier, label, summaries
                .Where(s => AccessHelpers.Tier(s.ScopeType) == tier)
                .OrderByDescending(s => s.Priority)
                .ThenBy(s => s.Dto.Name)
                .Select(s => s.Dto).ToList());

        var groups = new List<RoleGroupDto> { Group("enterprise", "Enterprise · HQ"), Group("franchise", "Franchise") };

        return new AccessRolesDto(
            matrix.Rows.Select(m => new MatrixModuleDto(m.Key, m.Label)).ToList(),
            PermissionMatrix.Actions.ToList(),
            groups);
    }
}
