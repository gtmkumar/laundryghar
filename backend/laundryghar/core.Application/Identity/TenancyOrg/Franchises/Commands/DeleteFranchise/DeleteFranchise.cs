using core.Application.Common.Interfaces;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.TenancyOrg.Franchises.Commands.DeleteFranchise;

public sealed record DeleteFranchiseCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public class DeleteFranchiseCommandHandler : ICommandHandler<DeleteFranchiseCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteFranchiseCommandHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<bool> HandleAsync(DeleteFranchiseCommand command, CancellationToken cancellationToken)
    {
        var f = await _db.Franchises.FindAsync([command.Id], cancellationToken);
        if (f is null) return false;
        if (!_user.IsWithinScope(brandId: f.BrandId, franchiseId: f.Id))
            throw new ForbiddenException("This franchise is outside your assigned scope.");
        f.DeletedAt = DateTimeOffset.UtcNow; f.UpdatedBy = command.ActorId; f.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
