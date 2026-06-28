using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Queries.GetAccessRoles;

public sealed record GetAccessRolesQuery : IQuery<AccessRolesDto>;

public class GetAccessRolesQueryHandler : IQueryHandler<GetAccessRolesQuery, AccessRolesDto>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;
    public GetAccessRolesQueryHandler(ICoreDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<AccessRolesDto> HandleAsync(GetAccessRolesQuery q, CancellationToken ct)
    {
        var matrix = await ModuleMatrix.LoadAsync(_db, ct);

        // System roles (BrandId == null) are global; custom roles are brand-scoped and must not
        // leak across tenants. Scope to the caller's active brand (X-Brand-Id / JWT); when no brand
        // context is resolvable (platform admin, no selection) fall back to the full set.
        var brandId = _user.TryGetBrandId();
        var brandVertical = brandId is { } bvId
            ? await _db.Brands.AsNoTracking().Where(b => b.Id == bvId).Select(b => b.VerticalKey).FirstOrDefaultAsync(ct)
            : null;

        var roles = await _db.Roles.AsNoTracking()
            .Where(r => r.DeletedAt == null && r.Status == "active"
                && (r.BrandId == null || brandId == null || r.BrandId == brandId))
            .Select(r => new
            {
                r.Id,
                r.Code,
                r.Name,
                r.Description,
                r.ScopeType,
                r.VerticalKey,
                r.IsSystem,
                r.Priority,
                PermCodes = r.RolePermissions.Select(rp => rp.Permission.Code).ToList(),
                MemberCount = r.UserScopeMemberships.Count(m => m.RevokedAt == null),
            })
            .ToListAsync(ct);

        // Vertical gate: a role tagged with a vertical_key (e.g. laundry warehouse_staff) is offered
        // only to brands of that vertical; a neutral (null) role shows to all; with no brand context
        // every role passes. Mirrors GetNavigator's module gating.
        roles = roles.Where(r => VerticalKey.IsAvailableTo(r.VerticalKey, brandVertical)).ToList();

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
                    r.MemberCount, onCells.OrderBy(c => c).ToList(), r.VerticalKey),
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

        // Build cellKey → permission codes (the fan-out), so the UI can show what a checkbox grants.
        var allCodes = await _db.Permissions.AsNoTracking().Select(p => p.Code).ToListAsync(ct);
        var cells = new Dictionary<string, List<string>>();
        foreach (var code in allCodes)
            foreach (var cell in matrix.CellsFor(PermissionMatrix.Module(code), PermissionMatrix.Action(code)))
                (cells.TryGetValue(cell, out var l) ? l : cells[cell] = []).Add(code);
        var cellMap = cells.ToDictionary(
            kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.OrderBy(c => c).ToList());

        return new AccessRolesDto(
            matrix.Rows.Select(m => new MatrixModuleDto(m.Key, m.Label)).ToList(),
            PermissionMatrix.Actions.ToList(),
            groups,
            cellMap);
    }
}
