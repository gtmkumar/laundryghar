using operations.Application.Common.Interfaces;

namespace operations.Application.Logistics.Common;

/// <summary>
/// Atomic helper for maintaining <c>logistics.riders.current_load</c>.
///
/// Call <see cref="IncrementAsync"/> when a new delivery_assignment enters an active
/// state ("accepted"/"assigned") and <see cref="DecrementAsync"/> when it reaches a
/// terminal state (completed / failed / cancelled).
///
/// Both methods use a guarded raw-SQL UPDATE (via the context's parameterized raw-SQL seam)
/// so they are:
///   - Atomic relative to concurrent riders finishing tasks simultaneously.
///   - Guard: current_load never goes below 0 (GREATEST(0, current_load - 1)).
///
/// <para>Ported from the shared <c>RiderLoadHelper</c> and re-targeted at
/// <see cref="IOperationsDbContext"/> so Application handlers never touch the concrete context.</para>
/// </summary>
public static class RiderLoad
{
    /// <summary>
    /// Increments <c>current_load</c> by 1 for the given rider. Call after the
    /// <c>SaveChangesAsync</c> that persists a newly-accepted delivery_assignment row.
    /// </summary>
    public static Task IncrementAsync(IOperationsDbContext db, Guid riderId, CancellationToken ct = default) =>
        db.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE logistics.riders
               SET current_load = current_load + 1,
                   updated_at   = NOW()
             WHERE id = {riderId}
            """,
            ct);

    /// <summary>
    /// Decrements <c>current_load</c> by 1 (floor 0) for the given rider. Call after the
    /// <c>SaveChangesAsync</c> that stamps completed_at / cancelled_at on a leg.
    /// </summary>
    public static Task DecrementAsync(IOperationsDbContext db, Guid riderId, CancellationToken ct = default) =>
        db.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE logistics.riders
               SET current_load = GREATEST(0, current_load - 1),
                   updated_at   = NOW()
             WHERE id = {riderId}
            """,
            ct);
}
