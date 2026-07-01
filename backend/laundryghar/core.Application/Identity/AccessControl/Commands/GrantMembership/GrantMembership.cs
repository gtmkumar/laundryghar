using core.Application.Common.Interfaces;
using core.Application.Identity.Users.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Commands.GrantMembership;

/// <summary>ActorId carries the calling user's identity for privilege-escalation checks.</summary>
public sealed record GrantMembershipCommand(GrantMembershipRequest Request, Guid? ActorId) : ICommand<MembershipDto>;

public class GrantMembershipCommandHandler : ICommandHandler<GrantMembershipCommand, MembershipDto>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _actor;
    public GrantMembershipCommandHandler(ICoreDbContext db, ICurrentUser actor) { _db = db; _actor = actor; }

    public async Task<MembershipDto> HandleAsync(GrantMembershipCommand cmd, CancellationToken ct)
    {
        var actor = _actor;

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

        // ── Defense-in-depth: a brand-scoped role MUST bind to a concrete brand ──
        // The UI sends it, but if it's missing fall back to the actor's own brand and
        // reject if neither is available. Persisting a brand membership with a null
        // scope issues a token with no brand_id, which locks the user out of every
        // tenant-scoped service (orders/warehouse/…) with a 401.
        var effectiveScopeId = cmd.Request.ScopeId;
        if (cmd.Request.ScopeType == laundryghar.SharedDataModel.Enums.ScopeType.Brand
            && effectiveScopeId is null)
        {
            effectiveScopeId = actor.BrandId
                ?? throw new laundryghar.Utilities.Exceptions.ValidationException(
                    new Dictionary<string, string[]> { ["scopeId"] = ["Brand-scoped roles require a brand id."] });
        }

        // ── RBAC §6 sub-brand scope guard: the target scope node must lie within the actor's assigned scope ──
        // Platform admins pass automatically; a brand/franchise/store/warehouse-scoped actor may only grant a
        // membership at a node that is ancestor-or-self of one of their own membership nodes. This supersedes
        // the brand-only H2b below: it closes the sub-brand escalation where an actor whose brand matches the
        // target could otherwise grant at a DIFFERENT franchise/store/warehouse within the same brand.
        if (!_actor.IsWithinScope(
                brandId:     cmd.Request.ScopeType == ScopeType.Brand     ? effectiveScopeId : null,
                franchiseId: cmd.Request.ScopeType == ScopeType.Franchise ? effectiveScopeId : null,
                storeId:     cmd.Request.ScopeType == ScopeType.Store     ? effectiveScopeId : null,
                warehouseId: cmd.Request.ScopeType == ScopeType.Warehouse ? effectiveScopeId : null))
        {
            throw new ForbiddenException("This membership is outside your assigned scope.");
        }

        // ── H2b: actor's scope must cover the target ScopeId ──
        // Platform admins bypass scope checks (they manage all brands).
        if (!actor.IsPlatformAdmin && effectiveScopeId.HasValue)
        {
            // Resolve the brand of the target scope
            Guid? targetBrandId = cmd.Request.ScopeType switch
            {
                laundryghar.SharedDataModel.Enums.ScopeType.Brand =>
                    effectiveScopeId,
                laundryghar.SharedDataModel.Enums.ScopeType.Franchise =>
                    await _db.Franchises.AsNoTracking()
                        .Where(f => f.Id == effectiveScopeId)
                        .Select(f => (Guid?)f.BrandId)
                        .FirstOrDefaultAsync(ct),
                laundryghar.SharedDataModel.Enums.ScopeType.Store =>
                    await _db.Stores.AsNoTracking()
                        .Where(s => s.Id == effectiveScopeId)
                        .Select(s => (Guid?)s.BrandId)
                        .FirstOrDefaultAsync(ct),
                laundryghar.SharedDataModel.Enums.ScopeType.Warehouse =>
                    await _db.Warehouses.AsNoTracking()
                        .Where(w => w.Id == effectiveScopeId)
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

            // Keep the user's denormalised home vertical in sync with their new primary brand
            // (null for a platform-scoped primary). Persisted in the same SaveChanges below.
            var homeVertical = await ResolveScopeVerticalAsync(cmd.Request.ScopeType, effectiveScopeId, ct);
            var targetUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == cmd.Request.UserId, ct);
            if (targetUser is not null)
            {
                targetUser.VerticalKey = homeVertical;
                targetUser.UpdatedAt = DateTimeOffset.UtcNow;
                targetUser.UpdatedBy = cmd.ActorId;
            }
        }

        var membership = new UserScopeMembership
        {
            Id        = Guid.NewGuid(),
            UserId    = cmd.Request.UserId,
            ScopeType = cmd.Request.ScopeType,
            ScopeId   = effectiveScopeId,
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

        // Invalidate the target user's existing tokens (live revocation).
        await Common.PermVersionBumper.BumpUserAsync(_db, cmd.Request.UserId, ct);

        return new MembershipDto(
            membership.Id, membership.UserId, membership.ScopeType, membership.ScopeId,
            membership.RoleId, targetRole.Code, membership.IsPrimary, membership.GrantedAt);
    }

    /// <summary>Resolve the vertical of the brand a scope belongs to (brand/franchise/store/warehouse
    /// → brand → vertical_key); null for a platform scope or an unresolvable id.</summary>
    private async Task<string?> ResolveScopeVerticalAsync(string scopeType, Guid? scopeId, CancellationToken ct)
    {
        Guid? brandId = scopeType switch
        {
            ScopeType.Brand     => scopeId,
            ScopeType.Franchise => await _db.Franchises.AsNoTracking().Where(f => f.Id == scopeId).Select(f => (Guid?)f.BrandId).FirstOrDefaultAsync(ct),
            ScopeType.Store     => await _db.Stores.AsNoTracking().Where(s => s.Id == scopeId).Select(s => (Guid?)s.BrandId).FirstOrDefaultAsync(ct),
            ScopeType.Warehouse => await _db.Warehouses.AsNoTracking().Where(w => w.Id == scopeId).Select(w => (Guid?)w.BrandId).FirstOrDefaultAsync(ct),
            _ => null,
        };
        if (brandId is null) return null;
        return await _db.Brands.AsNoTracking().Where(b => b.Id == brandId).Select(b => b.VerticalKey).FirstOrDefaultAsync(ct);
    }
}
