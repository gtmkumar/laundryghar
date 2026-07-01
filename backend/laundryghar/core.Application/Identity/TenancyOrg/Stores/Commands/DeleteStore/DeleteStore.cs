using core.Application.Common.Interfaces;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.TenancyOrg.Stores.Commands.DeleteStore;

public sealed record DeleteStoreCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public class DeleteStoreCommandHandler : ICommandHandler<DeleteStoreCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteStoreCommandHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<bool> HandleAsync(DeleteStoreCommand command, CancellationToken cancellationToken)
    {
        var s = await _db.Stores.FindAsync([command.Id], cancellationToken);
        if (s is null) return false;
        if (!_user.IsWithinScope(brandId: s.BrandId, franchiseId: s.FranchiseId, storeId: s.Id))
            throw new ForbiddenException("This store is outside your assigned scope.");
        s.DeletedAt = DateTimeOffset.UtcNow; s.UpdatedBy = command.ActorId; s.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
