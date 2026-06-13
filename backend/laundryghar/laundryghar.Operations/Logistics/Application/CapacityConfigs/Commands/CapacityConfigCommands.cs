using laundryghar.Logistics.Infrastructure.Auth;
using laundryghar.Logistics.Infrastructure.Services;
using FluentValidation;
using laundryghar.Logistics.Application.CapacityConfigs.Dtos;
using laundryghar.SharedDataModel.Enums;
using MediatR;

namespace laundryghar.Logistics.Application.CapacityConfigs.Commands;

// ── Create CapacityConfig ─────────────────────────────────────────────────────

public sealed record CreateCapacityConfigCommand(CreateCapacityConfigRequest Request, Guid? ActorId)
    : IRequest<RiderCapacityConfigDto>;

public sealed class CreateCapacityConfigHandler
    : IRequestHandler<CreateCapacityConfigCommand, RiderCapacityConfigDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateCapacityConfigHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RiderCapacityConfigDto> Handle(CreateCapacityConfigCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        // Verify rider belongs to this brand
        var riderExists = await _db.Riders
            .AnyAsync(r => r.Id == req.RiderId && r.BrandId == brandId, ct);
        if (!riderExists)
            throw new BusinessRuleException("Rider not found under the current brand.");

        // Cross-brand guard: an explicit store must belong to this brand too.
        if (req.StoreId is Guid storeId)
        {
            var storeExists = await _db.Stores
                .AnyAsync(s => s.Id == storeId && s.BrandId == brandId, ct);
            if (!storeExists)
                throw new BusinessRuleException("Store not found under the current brand.");
        }

        var config = new RiderCapacityConfig
        {
            Id                   = Guid.NewGuid(),
            RiderId              = req.RiderId,
            BrandId              = brandId,
            StoreId              = req.StoreId,
            DayOfWeek            = req.DayOfWeek,
            SlotStart            = req.SlotStart,
            SlotEnd              = req.SlotEnd,
            MaxPickupsPerSlot    = req.MaxPickupsPerSlot,
            MaxDeliveriesPerSlot = req.MaxDeliveriesPerSlot,
            MaxConcurrentOrders  = req.MaxConcurrentOrders,
            IsActive             = true,
            EffectiveFrom        = req.EffectiveFrom,
            EffectiveTo          = req.EffectiveTo,
            Status               = RiderCapacityConfigStatus.Active,
            CreatedAt            = now,
            UpdatedAt            = now,
            CreatedBy            = cmd.ActorId,
            UpdatedBy            = cmd.ActorId
        };

        _db.RiderCapacityConfigs.Add(config);
        await _db.SaveChangesAsync(ct);
        return ToDto(config);
    }

    internal static RiderCapacityConfigDto ToDto(RiderCapacityConfig c) => new(
        c.Id, c.RiderId, c.BrandId, c.StoreId,
        c.DayOfWeek, c.SlotStart, c.SlotEnd,
        c.MaxPickupsPerSlot, c.MaxDeliveriesPerSlot, c.MaxConcurrentOrders,
        c.IsActive, c.EffectiveFrom, c.EffectiveTo,
        c.Status, c.CreatedAt, c.UpdatedAt);
}

// ── Update CapacityConfig ─────────────────────────────────────────────────────

public sealed record UpdateCapacityConfigCommand(Guid Id, UpdateCapacityConfigRequest Request, Guid? ActorId)
    : IRequest<RiderCapacityConfigDto?>;

public sealed class UpdateCapacityConfigHandler
    : IRequestHandler<UpdateCapacityConfigCommand, RiderCapacityConfigDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateCapacityConfigHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RiderCapacityConfigDto?> Handle(UpdateCapacityConfigCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var config  = await _db.RiderCapacityConfigs
            .FirstOrDefaultAsync(c => c.Id == cmd.Id && c.BrandId == brandId, ct);
        if (config is null) return null;

        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        if (req.DayOfWeek           is not null) config.DayOfWeek           = req.DayOfWeek;
        if (req.SlotStart           is not null) config.SlotStart           = req.SlotStart;
        if (req.SlotEnd             is not null) config.SlotEnd             = req.SlotEnd;
        if (req.MaxPickupsPerSlot   is not null) config.MaxPickupsPerSlot   = req.MaxPickupsPerSlot.Value;
        if (req.MaxDeliveriesPerSlot is not null) config.MaxDeliveriesPerSlot = req.MaxDeliveriesPerSlot.Value;
        if (req.MaxConcurrentOrders is not null) config.MaxConcurrentOrders = req.MaxConcurrentOrders.Value;
        if (req.IsActive            is not null) config.IsActive            = req.IsActive.Value;
        if (req.EffectiveFrom       is not null) config.EffectiveFrom       = req.EffectiveFrom.Value;
        if (req.EffectiveTo         is not null) config.EffectiveTo         = req.EffectiveTo;
        if (req.Status              is not null) config.Status              = req.Status;

        config.UpdatedAt = now;
        config.UpdatedBy = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return CreateCapacityConfigHandler.ToDto(config);
    }
}

// ── Validators ────────────────────────────────────────────────────────────────

public sealed class CreateCapacityConfigValidator : AbstractValidator<CreateCapacityConfigCommand>
{
    public CreateCapacityConfigValidator()
    {
        RuleFor(x => x.Request.RiderId).NotEmpty();
        RuleFor(x => x.Request.DayOfWeek)
            .InclusiveBetween((short)0, (short)6)
            .When(x => x.Request.DayOfWeek.HasValue)
            .WithMessage("DayOfWeek must be 0 (Sunday) through 6 (Saturday).");
        RuleFor(x => x.Request.MaxPickupsPerSlot).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.MaxDeliveriesPerSlot).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.MaxConcurrentOrders).GreaterThanOrEqualTo(0);
    }
}
