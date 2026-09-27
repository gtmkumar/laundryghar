using core.Application.Common.Interfaces;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Common;

/// <summary>
/// The authority check for "may this actor change what this role is allowed to do" — one
/// implementation, shared by every handler that edits <c>role_permissions</c>.
///
/// <para><b>Why it exists.</b> The 2026-09-05 audit (A-1, "guard asymmetry") found the two write
/// paths into the same table were guarded differently. <c>AssignPermission</c> carried three
/// authority guards written inline; <c>SetRoleCells</c> — reachable from the access-control matrix,
/// behind the identical <c>permissions.assign</c> policy, and able to flip many codes at once —
/// carried none of them. A brand admin could not add a permission to <c>platform_admin</c> one
/// checkbox at a time, but could do it by saving a matrix cell. Two doors into one room, one of them
/// unlocked.</para>
///
/// <para>Duplication is what let them drift, so the rule now lives in exactly one place and both
/// callers invoke it. A third write path must call this too.</para>
///
/// <para>The rules mirror <c>GrantMembership.cs:150-185</c>, the house pattern for "may this actor
/// confer this authority".</para>
/// </summary>
public static class RoleEditGuard
{
    /// <summary>The facts about a role that the guards decide on. Projected rather than tracked so
    /// a caller cannot accidentally mutate the row it is being authorized against.</summary>
    public sealed record EditableRole(Guid Id, Guid? BrandId, int Priority, bool IsSystem, string Code);

    /// <summary>Loads a live (non-soft-deleted) role, or null. Callers decide how absence is
    /// reported — <c>AssignPermission</c> raises a field error, <c>SetRoleCells</c> returns 404 —
    /// so this deliberately does not throw.</summary>
    public static Task<EditableRole?> FindLiveRoleAsync(ICoreDbContext db, Guid roleId, CancellationToken ct) =>
        db.Roles.AsNoTracking()
            .Where(r => r.Id == roleId && r.DeletedAt == null)
            .Select(r => new EditableRole(r.Id, r.BrandId, r.Priority, r.IsSystem, r.Code))
            .FirstOrDefaultAsync(ct)!;

    /// <summary>
    /// Throws <see cref="ForbiddenException"/> unless <paramref name="actor"/> may edit
    /// <paramref name="role"/>'s permissions. A platform admin passes unconditionally — that is the
    /// existing model, not something this guard introduces.
    /// </summary>
    public static async Task EnsureMayEditAsync(
        ICoreDbContext db,
        ICurrentUser actor,
        Guid? actorId,
        EditableRole role,
        CancellationToken ct)
    {
        if (actor.IsPlatformAdmin) return;

        // ── Guard 1: only a platform admin may edit a system role ────────────────────────────
        // All seeded roles carry brand_id NULL; without this a brand admin could add permissions
        // to platform_admin itself. Both conditions are checked because they are independently
        // sufficient: a NULL brand means "not owned by any tenant", is_system means "shipped".
        if (role.BrandId is null || role.IsSystem)
            throw new ForbiddenException("Only a platform administrator may modify a system role.");

        // ── Guard 2: brand isolation ─────────────────────────────────────────────────────────
        if (role.BrandId != actor.BrandId)
            throw new ForbiddenException("You may only modify roles within your own brand.");

        // ── Guard 3: anti-escalation on role rank ────────────────────────────────────────────
        // Lower priority number = higher rank. Strict `<` matches GrantMembership.cs:180 — an actor
        // may edit a role of EQUAL rank, which is what lets a brand admin manage a peer role.
        var actorMinPriority = await db.UserScopeMemberships.AsNoTracking()
            .Where(m => m.UserId == actorId
                     && m.RevokedAt == null
                     && (m.ExpiresAt == null || m.ExpiresAt > DateTimeOffset.UtcNow))
            // Nullable projection so an actor with no live membership yields NULL from SQL MIN
            // rather than throwing, and falls through to the most restrictive rank. A null actorId
            // lands here too, so an unidentified caller can edit nothing — fail-closed.
            .Join(db.Roles.IgnoreQueryFilters(), m => m.RoleId, r => r.Id, (m, r) => (int?)r.Priority)
            .MinAsync(ct) ?? int.MaxValue;

        if (role.Priority < actorMinPriority)
            throw new ForbiddenException("You cannot modify a role with higher privileges than your own.");
    }
}
