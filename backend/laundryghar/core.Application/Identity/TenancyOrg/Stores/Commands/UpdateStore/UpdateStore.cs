using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.TenancyOrg.Stores.Commands.UpdateStore;

public sealed record UpdateStoreCommand(Guid Id, UpdateStoreRequest Request, Guid? ActorId) : ICommand<StoreDto?>;

public class UpdateStoreCommandHandler : ICommandHandler<UpdateStoreCommand, StoreDto?>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateStoreCommandHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<StoreDto?> HandleAsync(UpdateStoreCommand command, CancellationToken cancellationToken)
    {
        var s = await _db.Stores.FindAsync([command.Id], cancellationToken);
        if (s is null) return null;
        if (!_user.IsWithinScope(brandId: s.BrandId, franchiseId: s.FranchiseId, storeId: s.Id))
            throw new ForbiddenException("This store is outside your assigned scope.");
        if (command.Request.Name         is not null) s.Name         = command.Request.Name;
        if (command.Request.Status       is not null) s.Status       = command.Request.Status;
        if (command.Request.ContactPhone is not null) s.ContactPhone = command.Request.ContactPhone;
        s.UpdatedAt = DateTimeOffset.UtcNow; s.UpdatedBy = command.ActorId; s.Version++;
        await _db.SaveChangesAsync(cancellationToken);
        return new StoreDto(s.Id, s.BrandId, s.FranchiseId, s.Code, s.Name, s.StoreType, s.City, s.Status, s.CreatedAt);
    }
}
