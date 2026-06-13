using FluentValidation;
using laundryghar.Orders.Application.Delivery.Dtos;
using MediatR;

namespace laundryghar.Orders.Application.Delivery.Commands;

public sealed record CreateDeliverySlotCommand(CreateDeliverySlotRequest Request, Guid? ActorId)
    : IRequest<DeliverySlotDto>;

public sealed class CreateDeliverySlotHandler : IRequestHandler<CreateDeliverySlotCommand, DeliverySlotDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateDeliverySlotHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<DeliverySlotDto> Handle(CreateDeliverySlotCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        // Validate the store belongs to this brand (cross-brand IDOR guard).
        var storeInBrand = await _db.Stores
            .AnyAsync(s => s.Id == req.StoreId && s.BrandId == brandId, ct);
        if (!storeInBrand)
            throw new KeyNotFoundException("Store not found.");

        var slot = new DeliverySlot
        {
            Id           = Guid.NewGuid(),
            BrandId      = brandId,
            StoreId      = req.StoreId,
            SlotDate     = req.SlotDate,
            SlotStart    = req.SlotStart,
            SlotEnd      = req.SlotEnd,
            SlotType     = req.SlotType,
            Capacity     = req.Capacity,
            BookedCount  = 0,
            IsExpress    = req.IsExpress,
            IsActive     = true,
            Status       = "active",
            CreatedAt    = now,
            UpdatedAt    = now,
            CreatedBy    = cmd.ActorId,
            UpdatedBy    = cmd.ActorId
        };

        _db.DeliverySlots.Add(slot);
        await _db.SaveChangesAsync(ct);
        return ToDto(slot);
    }

    internal static DeliverySlotDto ToDto(DeliverySlot s) => new(
        s.Id, s.BrandId, s.StoreId, s.SlotDate, s.SlotStart, s.SlotEnd,
        s.SlotType, s.Capacity, s.BookedCount,
        s.Capacity - s.BookedCount, s.IsExpress, s.IsActive, s.Status);
}

public sealed record UpdateDeliverySlotCommand(Guid Id, UpdateDeliverySlotRequest Request, Guid? ActorId)
    : IRequest<DeliverySlotDto?>;

public sealed class UpdateDeliverySlotHandler : IRequestHandler<UpdateDeliverySlotCommand, DeliverySlotDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateDeliverySlotHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<DeliverySlotDto?> Handle(UpdateDeliverySlotCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var slot = await _db.DeliverySlots
            .FirstOrDefaultAsync(s => s.Id == cmd.Id && s.BrandId == brandId, ct);
        if (slot is null) return null;

        if (cmd.Request.Capacity.HasValue) slot.Capacity = cmd.Request.Capacity.Value;
        if (cmd.Request.IsActive.HasValue) slot.IsActive = cmd.Request.IsActive.Value;
        if (cmd.Request.Status is not null) slot.Status  = cmd.Request.Status;
        slot.UpdatedAt = DateTimeOffset.UtcNow;
        slot.UpdatedBy = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return CreateDeliverySlotHandler.ToDto(slot);
    }
}

public sealed class CreateDeliverySlotValidator : AbstractValidator<CreateDeliverySlotCommand>
{
    private static readonly string[] AllowedTypes = ["pickup", "delivery"];

    public CreateDeliverySlotValidator()
    {
        RuleFor(x => x.Request.StoreId).NotEmpty();
        RuleFor(x => x.Request.Capacity).GreaterThan(0);
        RuleFor(x => x.Request.SlotType)
            .Must(t => AllowedTypes.Contains(t))
            .WithMessage("SlotType must be 'pickup' or 'delivery'.");
    }
}

public sealed class UpdateDeliverySlotValidator : AbstractValidator<UpdateDeliverySlotCommand>
{
    private static readonly string[] AllowedStatuses = ["active", "inactive", "full", "cancelled"];

    public UpdateDeliverySlotValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Request.Capacity)
            .GreaterThan(0)
            .When(x => x.Request.Capacity.HasValue)
            .WithMessage("Capacity must be greater than 0.");
        RuleFor(x => x.Request.Status)
            .Must(s => AllowedStatuses.Contains(s))
            .When(x => x.Request.Status is not null)
            .WithMessage($"Status must be one of: {string.Join(", ", AllowedStatuses)}.");
    }
}
