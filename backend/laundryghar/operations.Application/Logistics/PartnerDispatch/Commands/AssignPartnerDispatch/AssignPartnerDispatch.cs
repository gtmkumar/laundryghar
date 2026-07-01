using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.PartnerDispatch.Common;
using operations.Application.Logistics.PartnerDispatch.Dtos;
using DispatchEntity = laundryghar.SharedDataModel.Entities.Logistics.PartnerDispatch;

namespace operations.Application.Logistics.PartnerDispatch.Commands.AssignPartnerDispatch;

/// <param name="Request">Booking + partner + rider to assign.</param>
/// <param name="ActorId">The brand-staff user creating the dispatch (JWT sub) — audit created_by.</param>
public sealed record AssignPartnerDispatchCommand(AssignPartnerDispatchRequest Request, Guid? ActorId)
    : ICommand<PartnerDispatchDto>;

/// <summary>
/// Staff/fleet path: creates a dispatch for a partner booking and assigns it to a rider
/// (Status → 'assigned'). Runs in a BRAND-STAFF session.
///
/// <para><b>Attribution is server-verified, never trusted from the request.</b> The combined
/// <c>rls_partner_or_brand</c> WITH CHECK only enforces the BRAND arm in a staff session (the partner
/// arm is inert with no partner_id), so it cannot stop a staff caller attributing a dispatch to an
/// arbitrary <c>partner_id</c> or a booking another brand serves. Because
/// <c>logistics.partner_bookings</c> is partner-RLS-scoped (a staff session reads zero bookings), the
/// handler performs a <b>controlled, transaction-scoped RLS bypass</b> (<c>SET LOCAL
/// app.bypass_rls='true'</c>, honoured by <c>kernel.rls_bypass()</c>) to read the ONE target booking,
/// then: derives <c>partner_id</c> FROM the booking (rejecting a mismatched request value); verifies
/// the booking's serving <c>brand_id</c> equals the acting staff brand — or, if the booking is
/// unclaimed, atomically claims it for the acting fleet. <c>brand_id</c> on the dispatch is the acting
/// staff's brand. The bypass is dropped again before the INSERT so its WITH CHECK still validates the
/// server-set brand_id (defence in depth).</para>
/// </summary>
public sealed class AssignPartnerDispatchHandler
    : ICommandHandler<AssignPartnerDispatchCommand, PartnerDispatchDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentTenant _tenant;

    public AssignPartnerDispatchHandler(IOperationsDbContext db, ICurrentTenant tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<PartnerDispatchDto> HandleAsync(AssignPartnerDispatchCommand cmd, CancellationToken ct)
    {
        // Acting brand-staff's brand — the fleet claiming/serving the booking. Also the value the
        // rls_partner_or_brand WITH CHECK brand arm validates on the dispatch INSERT.
        var brandId = _tenant.BrandId
            ?? throw new ForbiddenException("Brand context required to assign a partner dispatch.");

        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var assignsRider = req.RiderId is { } rid && rid != Guid.Empty;

        // PartnerId is intentionally NOT copied from the request here — it is derived from the booking
        // inside the transaction below and assigned once verified.
        var e = new DispatchEntity
        {
            Id               = Guid.NewGuid(),
            PartnerBookingId = req.PartnerBookingId,
            BrandId          = brandId,
            RiderId          = assignsRider ? req.RiderId : null,
            Status           = assignsRider ? PartnerDispatchMapper.Assigned : PartnerDispatchMapper.Pending,
            PickupOtp        = req.PickupOtp,
            DropOtp          = req.DropOtp,
            AssignedAt       = assignsRider ? now : null,
            CreatedAt        = now,
            UpdatedAt        = now,
            CreatedBy        = cmd.ActorId,
            UpdatedBy        = cmd.ActorId,
        };

        // Single atomic transaction: verify/claim the booking under a scoped RLS bypass, then insert
        // the dispatch. ExecuteInTransactionAsync wraps this in the Npgsql retrying execution strategy.
        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            // ── Controlled RLS-bypass read (integrity check only) ──────────────────────────────
            // logistics.partner_bookings is rls_partner-scoped, so a brand-staff session (partner_id
            // NULL) cannot SELECT it. SET LOCAL lifts RLS for THIS TRANSACTION ONLY (reverts on
            // commit) — kernel.rls_bypass() honours 'true' (harden_app_user_and_rls_bypass.sql).
            await _db.ExecuteSqlInterpolatedAsync($"SET LOCAL app.bypass_rls = 'true'", innerCt);

            var booking = await _db.PartnerBookings.AsNoTracking()
                .Where(b => b.Id == req.PartnerBookingId)
                .Select(b => new { b.PartnerId, b.BrandId })
                .FirstOrDefaultAsync(innerCt);

            // (a) Unknown booking → clean 404 (KeyNotFoundException maps to NotFound).
            if (booking is null)
                throw new KeyNotFoundException("Partner booking not found.");

            // (b) partner_id comes from the booking, NOT the request. A supplied value that disagrees
            //     is a client bug or a mis-attribution attempt — reject rather than silently trust it.
            if (req.PartnerId != booking.PartnerId)
                throw new ForbiddenException("PartnerId does not match the booking's owning partner.");
            e.PartnerId = booking.PartnerId;

            // (c) Brand attribution: the booking must be served by the acting staff's brand.
            if (booking.BrandId is { } servingBrand)
            {
                if (servingBrand != brandId)
                    throw new ForbiddenException("This booking is served by a different brand's fleet.");
            }
            else
            {
                // Unclaimed booking → the acting fleet claims it (natural "a brand's fleet picks up
                // the booking" flow). Guarded UPDATE under the same bypass; the brand_id IS NULL
                // predicate makes the claim atomic — 0 rows means another fleet claimed it first.
                var claimed = await _db.ExecuteSqlInterpolatedAsync(
                    $"UPDATE logistics.partner_bookings SET brand_id = {brandId}, updated_at = now() WHERE id = {req.PartnerBookingId} AND brand_id IS NULL",
                    innerCt);
                if (claimed == 0)
                    throw new ForbiddenException("This booking was just claimed by another brand's fleet.");
            }

            // ── Restore RLS before the INSERT so its WITH CHECK still validates brand_id ─────────
            await _db.ExecuteSqlInterpolatedAsync($"SET LOCAL app.bypass_rls = 'false'", innerCt);

            _db.PartnerDispatches.Add(e);
            await _db.SaveChangesAsync(innerCt);
        }, ct);

        return PartnerDispatchMapper.ToDto(e);
    }
}
