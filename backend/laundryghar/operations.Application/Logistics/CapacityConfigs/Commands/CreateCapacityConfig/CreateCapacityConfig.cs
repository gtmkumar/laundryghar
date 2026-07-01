using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.CapacityConfigs.Dtos;

namespace operations.Application.Logistics.CapacityConfigs.Commands.CreateCapacityConfig;

// ── Create CapacityConfig ─────────────────────────────────────────────────────

public sealed record CreateCapacityConfigCommand(CreateCapacityConfigRequest Request, Guid? ActorId)
    : ICommand<RiderCapacityConfigDto>;

public sealed class CreateCapacityConfigHandler
    : ICommandHandler<CreateCapacityConfigCommand, RiderCapacityConfigDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreateCapacityConfigHandler(IOperationsDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RiderCapacityConfigDto> HandleAsync(CreateCapacityConfigCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var req     = command.Request;
        var now     = DateTimeOffset.UtcNow;

        // Verify rider belongs to this brand
        var riderExists = await _db.Riders
            .AnyAsync(r => r.Id == req.RiderId && r.BrandId == brandId, cancellationToken);
        if (!riderExists)
            throw new BusinessRuleException("Rider not found under the current brand.");

        // Cross-brand guard: an explicit store must belong to this brand too.
        if (req.StoreId is Guid storeId)
        {
            var storeExists = await _db.Stores
                .AnyAsync(s => s.Id == storeId && s.BrandId == brandId, cancellationToken);
            if (!storeExists)
                throw new BusinessRuleException("Store not found under the current brand.");
        }

        if (!_user.IsWithinScope(brandId: brandId, storeId: req.StoreId))
            throw new ForbiddenException("This capacity config is outside your assigned scope.");

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
            CreatedBy            = command.ActorId,
            UpdatedBy            = command.ActorId
        };

        _db.RiderCapacityConfigs.Add(config);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(config);
    }

    internal static RiderCapacityConfigDto ToDto(RiderCapacityConfig c) => new(
        c.Id, c.RiderId, c.BrandId, c.StoreId,
        c.DayOfWeek, c.SlotStart, c.SlotEnd,
        c.MaxPickupsPerSlot, c.MaxDeliveriesPerSlot, c.MaxConcurrentOrders,
        c.IsActive, c.EffectiveFrom, c.EffectiveTo,
        c.Status, c.CreatedAt, c.UpdatedAt);
}

public sealed class CreateCapacityConfigRequestValidator : AbstractValidator<CreateCapacityConfigRequest>
{
    public CreateCapacityConfigRequestValidator()
    {
        RuleFor(x => x.RiderId).NotEmpty();
        RuleFor(x => x.DayOfWeek)
            .InclusiveBetween((short)0, (short)6)
            .When(x => x.DayOfWeek.HasValue)
            .WithMessage("DayOfWeek must be 0 (Sunday) through 6 (Saturday).");
        RuleFor(x => x.MaxPickupsPerSlot).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxDeliveriesPerSlot).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxConcurrentOrders).GreaterThanOrEqualTo(0);
    }
}
