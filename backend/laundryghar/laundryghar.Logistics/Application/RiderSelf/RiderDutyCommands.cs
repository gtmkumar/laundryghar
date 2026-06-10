using MediatR;

namespace laundryghar.Logistics.Application.RiderSelf;

// ── Duty-toggle request / response ───────────────────────────────────────────

/// <summary>Request body for PATCH /api/v1/rider/duty.</summary>
public sealed record SetDutyRequest(bool OnDuty);

/// <summary>Response returned by the duty toggle endpoint.</summary>
/// <param name="OnDuty">The new duty state persisted.</param>
/// <param name="OpenTaskCount">Number of delivery_assignments in a non-terminal status at
/// the time of the toggle. Non-zero when going OFF duty signals the rider still has work.</param>
public sealed record DutyToggleResponse(bool OnDuty, int OpenTaskCount);

// ── MediatR command + handler ─────────────────────────────────────────────────

/// <summary>
/// Toggles the authenticated rider's duty state.
///
/// Resolved: rider identified via UserId (from JWT sub) + BrandId (from JWT brand_id).
/// Going off duty with open tasks is allowed — the caller receives openTaskCount &gt; 0
/// as a warning signal; no 4xx is returned.
/// </summary>
public sealed record SetRiderDutyCommand(Guid UserId, Guid BrandId, bool OnDuty)
    : IRequest<DutyToggleResult>;

/// <summary>Discriminated result: rider not found or success with payload.</summary>
public sealed record DutyToggleResult(string Outcome, DutyToggleResponse? Data = null)
{
    public static DutyToggleResult NotFound()              => new("not_found");
    public static DutyToggleResult Ok(DutyToggleResponse d) => new("ok", d);
}

public sealed class SetRiderDutyHandler : IRequestHandler<SetRiderDutyCommand, DutyToggleResult>
{
    private readonly LaundryGharDbContext _db;

    public SetRiderDutyHandler(LaundryGharDbContext db) => _db = db;

    private static readonly string[] OpenStatuses =
        ["assigned", "accepted", "started", "arrived"];

    public async Task<DutyToggleResult> Handle(SetRiderDutyCommand cmd, CancellationToken ct)
    {
        // Self-resolve: rider is always identified by JWT sub (user_id) + brand_id claim.
        var rider = await _db.Riders
            .FirstOrDefaultAsync(r => r.UserId == cmd.UserId && r.BrandId == cmd.BrandId, ct);

        if (rider is null)
            return DutyToggleResult.NotFound();

        var now = DateTimeOffset.UtcNow;

        rider.IsOnDuty   = cmd.OnDuty;
        rider.OnDutySince = cmd.OnDuty ? now : null;
        rider.UpdatedAt   = now;

        // Count open delivery tasks so the caller can warn the rider when going off duty.
        // We query before SaveChanges so the state used for the count is consistent.
        int openTaskCount = 0;
        if (!cmd.OnDuty)
        {
            openTaskCount = await _db.DeliveryAssignments
                .CountAsync(a => a.RiderId == rider.Id
                              && a.BrandId == cmd.BrandId
                              && OpenStatuses.Contains(a.Status), ct);
        }

        await _db.SaveChangesAsync(ct);

        return DutyToggleResult.Ok(new DutyToggleResponse(cmd.OnDuty, openTaskCount));
    }
}
