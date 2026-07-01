using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.Exceptions;
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
/// <para>brand_id is sourced from the staff's brand context (<see cref="ICurrentTenant.BrandId"/>) —
/// the serving fleet's brand — NOT from a booking read. This is deliberate: logistics.partner_bookings
/// is partner-RLS-scoped, so a brand-staff session (no partner_id) cannot SELECT the booking; the
/// booking's serving brand IS this staff's brand. The <c>partner_dispatches</c> WITH CHECK then
/// passes via the BRAND arm (partner arm is inert because the session has no partner_id). The
/// partner_booking_id FK still guarantees the booking exists (RI runs as table owner, unaffected by
/// RLS). partner_id is taken from the request (shown on the incoming-booking card).</para>
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
        // Serving-fleet brand — required for the rls_partner_or_brand WITH CHECK brand arm to pass
        // in a staff session, and for the owning partner + brand staff to share visibility.
        var brandId = _tenant.BrandId
            ?? throw new ForbiddenException("Brand context required to assign a partner dispatch.");

        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var assignsRider = req.RiderId is { } rid && rid != Guid.Empty;

        var e = new DispatchEntity
        {
            Id               = Guid.NewGuid(),
            PartnerId        = req.PartnerId,
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

        _db.PartnerDispatches.Add(e);
        await _db.SaveChangesAsync(ct);

        return PartnerDispatchMapper.ToDto(e);
    }
}
