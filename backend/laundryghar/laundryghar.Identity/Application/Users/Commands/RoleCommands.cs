using laundryghar.Identity.Infrastructure.Services;
using MediatR;

namespace laundryghar.Identity.Application.Users.Commands;

// ─── DTOs ──────────────────────────────────────────────────────────────────

public sealed record RoleDto(Guid Id, string Code, string Name, string ScopeType, bool IsSystem, string Status);
public sealed record PermissionDto(Guid Id, string Code, string Module, string Action, string Name, string RiskLevel);
public sealed record MembershipDto(Guid Id, Guid UserId, string ScopeType, Guid? ScopeId, Guid RoleId, string RoleCode, bool IsPrimary, DateTimeOffset GrantedAt);

public sealed record AssignPermissionRequest(Guid RoleId, Guid PermissionId);
public sealed record GrantMembershipRequest(Guid UserId, string ScopeType, Guid? ScopeId, Guid RoleId, bool IsPrimary = false);
public sealed record RevokeMembershipRequest(Guid MembershipId, string? Reason = null);

// ─── Commands ─────────────────────────────────────────────────────────────

public sealed record GetRolesQuery(int Page = 1, int PageSize = 50) : IRequest<IReadOnlyList<RoleDto>>;
public sealed record GetPermissionsQuery(string? Module = null)     : IRequest<IReadOnlyList<PermissionDto>>;
public sealed record AssignPermissionCommand(AssignPermissionRequest Request, Guid? ActorId) : IRequest<bool>;
/// <summary>ActorContext carries the calling user's identity for privilege-escalation checks.</summary>
public sealed record GrantMembershipCommand(GrantMembershipRequest Request, Guid? ActorId, ICurrentUser Actor)  : IRequest<MembershipDto>;
public sealed record RevokeMembershipCommand(RevokeMembershipRequest Request, Guid? ActorId) : IRequest<bool>;

// ─── Handlers ─────────────────────────────────────────────────────────────

public sealed class GetRolesHandler : IRequestHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    private readonly LaundryGharDbContext _db;
    public GetRolesHandler(LaundryGharDbContext db) => _db = db;
    public async Task<IReadOnlyList<RoleDto>> Handle(GetRolesQuery r, CancellationToken ct) =>
        await _db.Roles.AsNoTracking().OrderBy(r => r.Priority)
            .Select(r => new RoleDto(r.Id, r.Code, r.Name, r.ScopeType, r.IsSystem, r.Status))
            .ToListAsync(ct);
}

public sealed class GetPermissionsHandler : IRequestHandler<GetPermissionsQuery, IReadOnlyList<PermissionDto>>
{
    private readonly LaundryGharDbContext _db;
    public GetPermissionsHandler(LaundryGharDbContext db) => _db = db;
    public async Task<IReadOnlyList<PermissionDto>> Handle(GetPermissionsQuery r, CancellationToken ct)
    {
        var q = _db.Permissions.AsNoTracking().Where(p => p.Status == "active");
        if (!string.IsNullOrEmpty(r.Module)) q = q.Where(p => p.Module == r.Module);
        return await q.OrderBy(p => p.Code).Select(p => new PermissionDto(p.Id, p.Code, p.Module, p.Action, p.Name, p.RiskLevel)).ToListAsync(ct);
    }
}

public sealed class AssignPermissionHandler : IRequestHandler<AssignPermissionCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    public AssignPermissionHandler(LaundryGharDbContext db) => _db = db;
    public async Task<bool> Handle(AssignPermissionCommand cmd, CancellationToken ct)
    {
        if (await _db.RolePermissions.AnyAsync(rp => rp.RoleId == cmd.Request.RoleId && rp.PermissionId == cmd.Request.PermissionId, ct))
            return true; // idempotent
        _db.RolePermissions.Add(new RolePermission
        {
            Id = Guid.NewGuid(), RoleId = cmd.Request.RoleId, PermissionId = cmd.Request.PermissionId,
            GrantedAt = DateTimeOffset.UtcNow, GrantedBy = cmd.ActorId, CreatedAt = DateTimeOffset.UtcNow, CreatedBy = cmd.ActorId
        });
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class GrantMembershipHandler : IRequestHandler<GrantMembershipCommand, MembershipDto>
{
    private readonly LaundryGharDbContext _db;
    public GrantMembershipHandler(LaundryGharDbContext db) => _db = db;

    public async Task<MembershipDto> Handle(GrantMembershipCommand cmd, CancellationToken ct)
    {
        var actor = cmd.Actor;

        // ── H2a: granting platform_admin role requires the actor to BE platform_admin ──
        var targetRole = await _db.Roles.FindAsync([cmd.Request.RoleId], ct)
            ?? throw new laundryghar.Utilities.Exceptions.ValidationException(
                new Dictionary<string, string[]> { ["roleId"] = ["Role not found."] });

        if (targetRole.Code == "platform_admin" &&
            actor.UserType != laundryghar.SharedDataModel.Enums.UserType.PlatformAdmin)
        {
            throw new UnauthorizedAccessException(
                "Only a platform_admin may grant the platform_admin role.");
        }

        // ── H2b: actor's scope must cover the target ScopeId ──
        // Platform admins bypass scope checks (they manage all brands).
        if (!actor.IsPlatformAdmin && cmd.Request.ScopeId.HasValue)
        {
            // Resolve the brand of the target scope
            Guid? targetBrandId = cmd.Request.ScopeType switch
            {
                laundryghar.SharedDataModel.Enums.ScopeType.Brand =>
                    cmd.Request.ScopeId,
                laundryghar.SharedDataModel.Enums.ScopeType.Franchise =>
                    await _db.Franchises.AsNoTracking()
                        .Where(f => f.Id == cmd.Request.ScopeId)
                        .Select(f => (Guid?)f.BrandId)
                        .FirstOrDefaultAsync(ct),
                laundryghar.SharedDataModel.Enums.ScopeType.Store =>
                    await _db.Stores.AsNoTracking()
                        .Where(s => s.Id == cmd.Request.ScopeId)
                        .Select(s => (Guid?)s.BrandId)
                        .FirstOrDefaultAsync(ct),
                laundryghar.SharedDataModel.Enums.ScopeType.Warehouse =>
                    await _db.Warehouses.AsNoTracking()
                        .Where(w => w.Id == cmd.Request.ScopeId)
                        .Select(w => (Guid?)w.BrandId)
                        .FirstOrDefaultAsync(ct),
                _ => null
            };

            // Actor's brand_id (from JWT active scope) must match the target scope's brand
            if (targetBrandId.HasValue && actor.BrandId != targetBrandId)
            {
                throw new UnauthorizedAccessException(
                    "You may only grant memberships within your own brand's scope.");
            }
        }

        // ── H2c: actor's role priority must be <= role being granted (lower number = higher rank) ──
        // Fetch actor's highest-privilege role (lowest priority number)
        if (!actor.IsPlatformAdmin)
        {
            var actorMinPriority = await _db.UserScopeMemberships
                .AsNoTracking()
                .Where(m => m.UserId == cmd.ActorId
                         && m.RevokedAt == null
                         && (m.ExpiresAt == null || m.ExpiresAt > DateTimeOffset.UtcNow))
                .Join(_db.Roles.IgnoreQueryFilters(),
                      m => m.RoleId,
                      r => r.Id,
                      (m, r) => r.Priority)
                .MinAsync(ct);   // lower priority number = higher rank

            if (targetRole.Priority < actorMinPriority)
            {
                throw new UnauthorizedAccessException(
                    "You cannot grant a role with higher privileges than your own.");
            }
        }

        // ── Apply primary flag ─────────────────────────────────────────────────
        if (cmd.Request.IsPrimary)
        {
            var existingPrimary = await _db.UserScopeMemberships
                .Where(m => m.UserId == cmd.Request.UserId && m.IsPrimary && m.RevokedAt == null)
                .ToListAsync(ct);
            existingPrimary.ForEach(m => m.IsPrimary = false);
        }

        var membership = new UserScopeMembership
        {
            Id        = Guid.NewGuid(),
            UserId    = cmd.Request.UserId,
            ScopeType = cmd.Request.ScopeType,
            ScopeId   = cmd.Request.ScopeId,
            RoleId    = cmd.Request.RoleId,
            IsPrimary = cmd.Request.IsPrimary,
            GrantedAt = DateTimeOffset.UtcNow,
            GrantedBy = cmd.ActorId,
            Metadata  = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = cmd.ActorId
        };
        _db.UserScopeMemberships.Add(membership);
        await _db.SaveChangesAsync(ct);

        return new MembershipDto(
            membership.Id, membership.UserId, membership.ScopeType, membership.ScopeId,
            membership.RoleId, targetRole.Code, membership.IsPrimary, membership.GrantedAt);
    }
}

public sealed class RevokeMembershipHandler : IRequestHandler<RevokeMembershipCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    public RevokeMembershipHandler(LaundryGharDbContext db) => _db = db;
    public async Task<bool> Handle(RevokeMembershipCommand cmd, CancellationToken ct)
    {
        var m = await _db.UserScopeMemberships.FindAsync([cmd.Request.MembershipId], ct);
        if (m is null || m.RevokedAt.HasValue) return false;
        m.RevokedAt = DateTimeOffset.UtcNow; m.RevokedBy = cmd.ActorId; m.RevokedReason = cmd.Request.Reason;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
