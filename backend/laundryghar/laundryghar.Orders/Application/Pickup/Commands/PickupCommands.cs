using FluentValidation;
using laundryghar.Orders.Application.Pickup.Dtos;
using MediatR;

namespace laundryghar.Orders.Application.Pickup.Commands;

// ── Admin: create pickup request ──────────────────────────────────────────────

public sealed record CreatePickupRequestAdminCommand(
    CreatePickupRequestRequest Request,
    Guid CustomerId,
    Guid? ActorId
) : IRequest<PickupRequestDto>;

public sealed class CreatePickupRequestAdminHandler
    : IRequestHandler<CreatePickupRequestAdminCommand, PickupRequestDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreatePickupRequestAdminHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<PickupRequestDto> Handle(CreatePickupRequestAdminCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        return await CreatePickup(_db, brandId, _user.StoreId, cmd.CustomerId, cmd.Request, cmd.ActorId, ct);
    }

    internal static async Task<PickupRequestDto> CreatePickup(
        LaundryGharDbContext db,
        Guid brandId, Guid? storeId, Guid customerId,
        CreatePickupRequestRequest req, Guid? actorId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var count = await db.PickupRequests
            .CountAsync(p => p.BrandId == brandId, ct);
        var requestNumber = $"PKP-{now.Year}-{brandId.ToString()[..4].ToUpper()}-{(count + 1):D6}";

        var entity = new PickupRequest
        {
            Id                 = Guid.NewGuid(),
            RequestNumber      = requestNumber,
            BrandId            = brandId,
            StoreId            = storeId,
            CustomerId         = customerId,
            AddressId          = req.AddressId,
            PickupSlotId       = req.SlotId,
            PickupDate         = req.PickupDate,
            PickupWindowStart  = req.PickupWindowStart,
            PickupWindowEnd    = req.PickupWindowEnd,
            IsExpress          = req.IsExpress,
            EstimatedItems     = req.EstimatedItems,
            EstimatedAmount    = req.EstimatedAmount,
            ServicesRequested  = req.ServicesRequested,
            CustomerNotes      = req.CustomerNotes,
            Status             = "pending",
            Metadata           = "{}",
            CreatedAt          = now,
            UpdatedAt          = now,
            CreatedBy          = actorId,
            UpdatedBy          = actorId
        };

        db.PickupRequests.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    internal static PickupRequestDto ToDto(PickupRequest p) => new(
        p.Id, p.RequestNumber, p.BrandId, p.StoreId, p.CustomerId, p.AddressId,
        p.PickupSlotId, p.PickupDate, p.PickupWindowStart, p.PickupWindowEnd,
        p.IsExpress, p.EstimatedItems, p.Status, p.CreatedAt);
}

// ── Admin: assign pickup to rider ─────────────────────────────────────────────

public sealed record AssignPickupCommand(Guid PickupRequestId, AssignPickupRequest Request, Guid? ActorId)
    : IRequest<DeliveryAssignmentDto?>;

public sealed class AssignPickupHandler : IRequestHandler<AssignPickupCommand, DeliveryAssignmentDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public AssignPickupHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<DeliveryAssignmentDto?> Handle(AssignPickupCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var pr = await _db.PickupRequests
            .FirstOrDefaultAsync(p => p.Id == cmd.PickupRequestId && p.BrandId == brandId, ct);
        if (pr is null) return null;

        var now = DateTimeOffset.UtcNow;
        var storeId = pr.StoreId ?? _user.StoreId ?? Guid.Empty;

        var assignment = new DeliveryAssignment
        {
            Id              = Guid.NewGuid(),
            BrandId         = brandId,
            StoreId         = storeId,
            RiderId         = cmd.Request.RiderId,
            PickupRequestId = cmd.PickupRequestId,
            LegType         = "pickup",
            AssignedAt      = now,
            AssignedBy      = cmd.ActorId,
            AddressSnapshot = "{}",
            OtpVerified     = false,
            Status          = "assigned",
            Metadata        = "{}",
            CreatedAt       = now,
            UpdatedAt       = now,
            CreatedBy       = cmd.ActorId,
            UpdatedBy       = cmd.ActorId
        };

        pr.Status    = "assigned";
        pr.UpdatedAt = now;
        pr.UpdatedBy = cmd.ActorId;

        _db.DeliveryAssignments.Add(assignment);
        await _db.SaveChangesAsync(ct);

        return new DeliveryAssignmentDto(
            assignment.Id, assignment.BrandId, assignment.StoreId, assignment.RiderId,
            assignment.OrderId, assignment.PickupRequestId, assignment.LegType,
            assignment.AssignedAt, assignment.Status);
    }
}

// ── Customer: schedule pickup with atomic slot booking ───────────────────────

public sealed record CustomerSchedulePickupCommand(
    Guid CustomerId, Guid BrandId,
    CreatePickupRequestRequest Request, Guid? ActorId
) : IRequest<PickupRequestDto>;

public sealed class CustomerSchedulePickupHandler
    : IRequestHandler<CustomerSchedulePickupCommand, PickupRequestDto>
{
    private readonly LaundryGharDbContext _db;

    public CustomerSchedulePickupHandler(LaundryGharDbContext db) => _db = db;

    public async Task<PickupRequestDto> Handle(CustomerSchedulePickupCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        // Atomic slot capacity update — prevents overbooking.
        // UPDATE delivery_slots SET booked_count = booked_count + 1
        // WHERE id = @id AND booked_count < capacity AND is_active = true
        // RETURNING id
        if (req.SlotId.HasValue)
        {
            var updated = await _db.Database.ExecuteSqlAsync(
                $"""
                UPDATE order_lifecycle.delivery_slots
                   SET booked_count = booked_count + 1, updated_at = NOW()
                 WHERE id = {req.SlotId.Value}
                   AND booked_count < capacity
                   AND is_active = true
                   AND brand_id = {cmd.BrandId}
                """, ct);

            if (updated == 0)
                throw new BusinessRuleException(
                    "The selected slot is full or unavailable. Please choose a different time slot.");

            // Record slot booking
            var slot = await _db.DeliverySlots
                .FirstOrDefaultAsync(s => s.Id == req.SlotId.Value, ct);

            if (slot is not null)
            {
                var booking = new DeliverySlotBooking
                {
                    Id          = Guid.NewGuid(),
                    SlotId      = req.SlotId.Value,
                    BrandId     = cmd.BrandId,
                    StoreId     = slot.StoreId,
                    CustomerId  = cmd.CustomerId,
                    BookingType = "pickup",
                    BookedAt    = now,
                    Status      = "active",
                    CreatedAt   = now,
                    CreatedBy   = cmd.CustomerId
                };
                _db.DeliverySlotBookings.Add(booking);
            }
        }

        // Resolve store from slot or brand's default store
        Guid? storeId = null;
        if (req.SlotId.HasValue)
        {
            var slot = await _db.DeliverySlots
                .Where(s => s.Id == req.SlotId.Value)
                .Select(s => s.StoreId)
                .FirstOrDefaultAsync(ct);
            storeId = slot == Guid.Empty ? null : slot;
        }

        var pickup = await CreatePickupRequestAdminHandler.CreatePickup(
            _db, cmd.BrandId, storeId, cmd.CustomerId, req, cmd.CustomerId, ct);

        // Link slot booking to pickup request
        if (req.SlotId.HasValue)
        {
            var booking = await _db.DeliverySlotBookings
                .FirstOrDefaultAsync(b => b.SlotId == req.SlotId.Value
                                       && b.CustomerId == cmd.CustomerId
                                       && b.PickupRequestId == null, ct);
            if (booking is not null)
            {
                booking.PickupRequestId = pickup.Id;
                await _db.SaveChangesAsync(ct);
            }
        }

        return pickup;
    }
}

public sealed class CustomerSchedulePickupValidator : AbstractValidator<CustomerSchedulePickupCommand>
{
    public CustomerSchedulePickupValidator()
    {
        RuleFor(x => x.Request.AddressId).NotEmpty();
        RuleFor(x => x.Request.PickupDate).NotEmpty();
    }
}
