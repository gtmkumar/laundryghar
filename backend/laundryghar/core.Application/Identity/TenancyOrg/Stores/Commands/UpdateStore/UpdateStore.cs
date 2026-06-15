using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.TenancyOrg.Stores.Commands.UpdateStore;

public sealed record UpdateStoreCommand(Guid Id, UpdateStoreRequest Request, Guid? ActorId) : ICommand<StoreDto?>;

public class UpdateStoreCommandHandler : ICommandHandler<UpdateStoreCommand, StoreDto?>
{
    private readonly ICoreDbContext _db;

    public UpdateStoreCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<StoreDto?> HandleAsync(UpdateStoreCommand command, CancellationToken cancellationToken)
    {
        var s = await _db.Stores.FindAsync([command.Id], cancellationToken);
        if (s is null) return null;
        if (command.Request.Name         is not null) s.Name         = command.Request.Name;
        if (command.Request.Status       is not null) s.Status       = command.Request.Status;
        if (command.Request.ContactPhone is not null) s.ContactPhone = command.Request.ContactPhone;
        s.UpdatedAt = DateTimeOffset.UtcNow; s.UpdatedBy = command.ActorId; s.Version++;
        await _db.SaveChangesAsync(cancellationToken);
        return new StoreDto(s.Id, s.BrandId, s.FranchiseId, s.Code, s.Name, s.StoreType, s.City, s.Status, s.CreatedAt);
    }
}
