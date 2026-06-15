using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.TenancyOrg.Stores.Commands.DeleteStore;

public sealed record DeleteStoreCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public class DeleteStoreCommandHandler : ICommandHandler<DeleteStoreCommand, bool>
{
    private readonly ICoreDbContext _db;

    public DeleteStoreCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<bool> HandleAsync(DeleteStoreCommand command, CancellationToken cancellationToken)
    {
        var s = await _db.Stores.FindAsync([command.Id], cancellationToken);
        if (s is null) return false;
        s.DeletedAt = DateTimeOffset.UtcNow; s.UpdatedBy = command.ActorId; s.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
