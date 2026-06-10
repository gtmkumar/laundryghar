using FluentValidation;
using laundryghar.Orders.Application.Pickup.Dtos;
using MediatR;

namespace laundryghar.Orders.Application.Delivery.Commands;

public sealed record CreateDeliveryAssignmentCommand(
    CreateDeliveryAssignmentRequest Request, Guid? ActorId) : IRequest<DeliveryAssignmentDto>;

public sealed class CreateDeliveryAssignmentHandler
    : IRequestHandler<CreateDeliveryAssignmentCommand, DeliveryAssignmentDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateDeliveryAssignmentHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<DeliveryAssignmentDto> Handle(CreateDeliveryAssignmentCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var storeId = _user.StoreId ?? Guid.Empty;
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        // Validate the assigned rider belongs to this brand (cross-brand IDOR guard).
        var riderInBrand = await _db.Riders
            .AnyAsync(r => r.Id == req.RiderId && r.BrandId == brandId, ct);
        if (!riderInBrand)
            throw new KeyNotFoundException("Rider not found.");

        // Validate the linked order belongs to this brand, if supplied.
        if (req.OrderId.HasValue)
        {
            var orderInBrand = await _db.Orders
                .AnyAsync(o => o.Id == req.OrderId.Value && o.BrandId == brandId, ct);
            if (!orderInBrand)
                throw new KeyNotFoundException("Order not found.");
        }

        // Validate the linked pickup request belongs to this brand, if supplied.
        if (req.PickupRequestId.HasValue)
        {
            var pickupInBrand = await _db.PickupRequests
                .AnyAsync(p => p.Id == req.PickupRequestId.Value && p.BrandId == brandId, ct);
            if (!pickupInBrand)
                throw new KeyNotFoundException("Pickup request not found.");
        }

        var assignment = new DeliveryAssignment
        {
            Id              = Guid.NewGuid(),
            BrandId         = brandId,
            StoreId         = storeId,
            RiderId         = req.RiderId,
            OrderId         = req.OrderId,
            OrderCreatedAt  = req.OrderCreatedAt,
            PickupRequestId = req.PickupRequestId,
            LegType         = req.LegType,
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

        _db.DeliveryAssignments.Add(assignment);
        await _db.SaveChangesAsync(ct);

        return new DeliveryAssignmentDto(
            assignment.Id, assignment.BrandId, assignment.StoreId, assignment.RiderId,
            assignment.OrderId, assignment.PickupRequestId, assignment.LegType,
            assignment.AssignedAt, assignment.Status);
    }
}

public sealed record UpdateDeliveryAssignmentCommand(Guid Id, UpdateDeliveryAssignmentRequest Request, Guid? ActorId)
    : IRequest<DeliveryAssignmentDto?>;

public sealed class UpdateDeliveryAssignmentHandler
    : IRequestHandler<UpdateDeliveryAssignmentCommand, DeliveryAssignmentDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateDeliveryAssignmentHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<DeliveryAssignmentDto?> Handle(UpdateDeliveryAssignmentCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.DeliveryAssignments
            .FirstOrDefaultAsync(a => a.Id == cmd.Id && a.BrandId == brandId, ct);
        if (e is null) return null;

        e.Status    = cmd.Request.Status;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return new DeliveryAssignmentDto(
            e.Id, e.BrandId, e.StoreId, e.RiderId, e.OrderId,
            e.PickupRequestId, e.LegType, e.AssignedAt, e.Status);
    }
}

public sealed class CreateDeliveryAssignmentValidator : AbstractValidator<CreateDeliveryAssignmentCommand>
{
    private static readonly string[] AllowedLegTypes = ["pickup", "delivery", "return"];

    public CreateDeliveryAssignmentValidator()
    {
        RuleFor(x => x.Request.RiderId).NotEmpty();
        RuleFor(x => x.Request.LegType)
            .Must(t => AllowedLegTypes.Contains(t))
            .WithMessage($"LegType must be one of: {string.Join(", ", AllowedLegTypes)}.");
    }
}
