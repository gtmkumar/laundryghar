using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.SharedDataModel.Entities.Logistics;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.PartnerBookings.Common;
using operations.Application.Logistics.PartnerBookings.Dtos;

namespace operations.Application.Logistics.PartnerBookings.Commands.CreatePartnerBooking;

/// <param name="Request">The pickup/drop snapshot + quoted fare.</param>
/// <param name="ActorId">The partner user raising the booking (JWT sub) — becomes created_by_partner_user_id.</param>
public sealed record CreatePartnerBookingCommand(CreatePartnerBookingRequest Request, Guid? ActorId)
    : ICommand<PartnerBookingDto>;

/// <summary>
/// Creates a RaaS partner booking. partner_id is sourced from the tenant context (the partner_id
/// claim), NOT the request: the rls_partner WITH CHECK on partner_bookings rejects any insert whose
/// partner_id differs from app.current_partner_id, so a cross-partner booking is impossible.
/// </summary>
public sealed class CreatePartnerBookingHandler : ICommandHandler<CreatePartnerBookingCommand, PartnerBookingDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentTenant _tenant;

    public CreatePartnerBookingHandler(IOperationsDbContext db, ICurrentTenant tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<PartnerBookingDto> HandleAsync(CreatePartnerBookingCommand cmd, CancellationToken ct)
    {
        var partnerId = _tenant.PartnerId
            ?? throw new UnauthorizedAccessException("Partner context required.");
        var partnerUserId = cmd.ActorId
            ?? throw new UnauthorizedAccessException("Partner user context required.");

        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var e = new PartnerBooking
        {
            Id                     = Guid.NewGuid(),
            PartnerId              = partnerId,
            BrandId                = req.BrandId,
            CreatedByPartnerUserId = partnerUserId,
            PickupSnapshot         = PartnerBookingMapper.Serialize(req.Pickup),
            DropSnapshot           = PartnerBookingMapper.Serialize(req.Drop),
            QuotedFare             = req.QuotedFare,
            Status                 = "requested",
            CreatedAt              = now,
            UpdatedAt              = now,
            CreatedBy              = partnerUserId,
            UpdatedBy              = partnerUserId,
        };

        _db.PartnerBookings.Add(e);
        await _db.SaveChangesAsync(ct);

        return PartnerBookingMapper.ToDto(e);
    }
}
