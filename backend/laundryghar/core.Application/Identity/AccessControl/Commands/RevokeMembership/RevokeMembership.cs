using core.Application.Common.Interfaces;
using core.Application.Identity.Users.Dtos;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Auth.Audit;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Commands.RevokeMembership;

public sealed record RevokeMembershipCommand(RevokeMembershipRequest Request, Guid? ActorId) : ICommand<bool>;

public class RevokeMembershipCommandHandler : ICommandHandler<RevokeMembershipCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IAuditWriter _audit;
    public RevokeMembershipCommandHandler(ICoreDbContext db, ICurrentUser user, IAuditWriter audit)
    {
        _db = db;
        _user = user;
        _audit = audit;
    }

    public async Task<bool> HandleAsync(RevokeMembershipCommand cmd, CancellationToken ct)
    {
        var m = await _db.UserScopeMemberships.FindAsync([cmd.Request.MembershipId], ct);
        if (m is null || m.RevokedAt.HasValue) return false;

        // ── Resolve the membership's FULL ancestor chain (brand → franchise → store/warehouse) ──
        // The scope guard is ancestor-or-self, so a brand/franchise admin revoking a membership at a
        // resource UNDER their own node must match via an ANCESTOR id. Passing only the leaf slot
        // (e.g. storeId, brandId=null) makes a brand_admin fail Matches(B, null) and get a false 403.
        // Resolve the target's brand (and franchise for store/warehouse) up-front and feed every level.
        // Platform-scoped memberships (ScopeId null) still require a platform admin.
        Guid? targetBrandId = null, targetFranchiseId = null, targetStoreId = null, targetWarehouseId = null;
        switch (m.ScopeType)
        {
            case ScopeType.Brand:
                targetBrandId = m.ScopeId;
                break;
            case ScopeType.Franchise:
                targetFranchiseId = m.ScopeId;
                targetBrandId = await _db.Franchises.AsNoTracking()
                    .Where(f => f.Id == m.ScopeId)
                    .Select(f => (Guid?)f.BrandId)
                    .FirstOrDefaultAsync(ct);
                break;
            case ScopeType.Store:
                targetStoreId = m.ScopeId;
                var storeChain = await _db.Stores.AsNoTracking()
                    .Where(s => s.Id == m.ScopeId)
                    .Select(s => new { s.BrandId, s.FranchiseId })
                    .FirstOrDefaultAsync(ct);
                targetBrandId = storeChain?.BrandId;
                targetFranchiseId = storeChain?.FranchiseId;
                break;
            case ScopeType.Warehouse:
                targetWarehouseId = m.ScopeId;
                var whChain = await _db.Warehouses.AsNoTracking()
                    .Where(w => w.Id == m.ScopeId)
                    .Select(w => new { w.BrandId, w.FranchiseId })
                    .FirstOrDefaultAsync(ct);
                targetBrandId = whChain?.BrandId;
                targetFranchiseId = whChain?.FranchiseId;
                break;
        }

        // RBAC sub-brand scope guard: the membership is loaded by id (bypassing query filters),
        // so the caller must sit at-or-above the membership's scope node before revoking it.
        // Scope is polymorphic (ScopeType + ScopeId); a platform-scoped membership (ScopeId null)
        // may only be revoked by a platform admin.
        if (m.ScopeType == ScopeType.Platform
            ? !_user.IsPlatformAdmin
            : !_user.IsWithinScope(
                brandId: targetBrandId,
                franchiseId: targetFranchiseId,
                storeId: targetStoreId,
                warehouseId: targetWarehouseId))
        {
            throw new ForbiddenException("This membership is outside your assigned scope.");
        }

        m.RevokedAt = DateTimeOffset.UtcNow; m.RevokedBy = cmd.ActorId; m.RevokedReason = cmd.Request.Reason;
        await _db.SaveChangesAsync(ct);

        // Invalidate the user's existing tokens (live revocation).
        await core.Application.Identity.Common.PermVersionBumper.BumpUserAsync(_db, m.UserId, ct);

        // Semantic audit: privilege revocation — which membership (user/scope/role) was pulled.
        await _audit.WriteAsync("membership.revoke", "user_scope_memberships", m.Id,
            resourceDisplay: $"Revoked membership @ {m.ScopeType}",
            oldValues: new { m.UserId, m.ScopeType, m.ScopeId, m.RoleId, Reason = cmd.Request.Reason },
            ct: ct);
        return true;
    }
}
