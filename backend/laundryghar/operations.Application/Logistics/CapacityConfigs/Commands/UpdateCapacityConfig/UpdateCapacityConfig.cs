using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.CapacityConfigs.Commands.CreateCapacityConfig;
using operations.Application.Logistics.CapacityConfigs.Dtos;

namespace operations.Application.Logistics.CapacityConfigs.Commands.UpdateCapacityConfig;

// ── Update CapacityConfig ─────────────────────────────────────────────────────

public sealed record UpdateCapacityConfigCommand(Guid Id, UpdateCapacityConfigRequest Request, Guid? ActorId)
    : ICommand<RiderCapacityConfigDto?>;

public sealed class UpdateCapacityConfigHandler
    : ICommandHandler<UpdateCapacityConfigCommand, RiderCapacityConfigDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateCapacityConfigHandler(IOperationsDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RiderCapacityConfigDto?> HandleAsync(UpdateCapacityConfigCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var config  = await _db.RiderCapacityConfigs
            .FirstOrDefaultAsync(c => c.Id == command.Id && c.BrandId == brandId, cancellationToken);
        if (config is null) return null;

        if (!_user.IsWithinScope(brandId: config.BrandId, storeId: config.StoreId))
            throw new ForbiddenException("This capacity config is outside your assigned scope.");

        var req = command.Request;
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
        config.UpdatedBy = command.ActorId;

        await _db.SaveChangesAsync(cancellationToken);
        return CreateCapacityConfigHandler.ToDto(config);
    }
}
