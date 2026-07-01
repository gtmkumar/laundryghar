using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Riders.Commands.CreateRider;
using operations.Application.Logistics.Riders.Dtos;

namespace operations.Application.Logistics.Riders.Commands.DeactivateRider;

// ── Deactivate Rider ──────────────────────────────────────────────────────────

public sealed record DeactivateRiderCommand(Guid Id, Guid? ActorId) : ICommand<RiderDto?>;

public sealed class DeactivateRiderHandler : ICommandHandler<DeactivateRiderCommand, RiderDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public DeactivateRiderHandler(IOperationsDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RiderDto?> HandleAsync(DeactivateRiderCommand command, CancellationToken cancellationToken)
    {
        var ct      = cancellationToken;
        var brandId = _user.RequireBrandId();
        var rider   = await _db.Riders
            .FirstOrDefaultAsync(r => r.Id == command.Id && r.BrandId == brandId, ct);
        if (rider is null) return null;

        if (!_user.IsWithinScope(brandId: rider.BrandId, franchiseId: rider.FranchiseId, storeId: rider.PrimaryStoreId))
            throw new ForbiddenException("This rider is outside your assigned scope.");

        // Franchise scoping (defense-in-depth): franchise-scoped actors must not
        // deactivate riders that belong to a different franchise.
        if (_user.FranchiseId is Guid fid && rider.FranchiseId != fid) return null;

        var now = DateTimeOffset.UtcNow;
        rider.Status    = RiderStatus.Terminated;
        rider.DeletedAt = now;
        rider.UpdatedAt = now;
        rider.UpdatedBy = command.ActorId;

        await _db.SaveChangesAsync(ct);
        var dto = await CreateRiderHandler.LoadEnrichedAsync(_db, rider, ct);
        return RiderDtoFinancialMask.Apply(dto, _user);
    }
}
