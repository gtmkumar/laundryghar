using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.TenancyOrg.Franchises.Commands.DeleteFranchise;

public sealed record DeleteFranchiseCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public class DeleteFranchiseCommandHandler : ICommandHandler<DeleteFranchiseCommand, bool>
{
    private readonly ICoreDbContext _db;

    public DeleteFranchiseCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<bool> HandleAsync(DeleteFranchiseCommand command, CancellationToken cancellationToken)
    {
        var f = await _db.Franchises.FindAsync([command.Id], cancellationToken);
        if (f is null) return false;
        f.DeletedAt = DateTimeOffset.UtcNow; f.UpdatedBy = command.ActorId; f.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
