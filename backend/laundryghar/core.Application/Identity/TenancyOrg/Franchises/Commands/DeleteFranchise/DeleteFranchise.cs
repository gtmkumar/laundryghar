using core.Application.Common.Interfaces;
using laundryghar.Utilities.Auth.Audit;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.TenancyOrg.Franchises.Commands.DeleteFranchise;

public sealed record DeleteFranchiseCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public class DeleteFranchiseCommandHandler : ICommandHandler<DeleteFranchiseCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IAuditWriter _audit;

    public DeleteFranchiseCommandHandler(ICoreDbContext db, ICurrentUser user, IAuditWriter audit)
    {
        _db = db;
        _user = user;
        _audit = audit;
    }

    public async Task<bool> HandleAsync(DeleteFranchiseCommand command, CancellationToken cancellationToken)
    {
        var f = await _db.Franchises.FindAsync([command.Id], cancellationToken);
        if (f is null) return false;
        if (!_user.IsWithinScope(brandId: f.BrandId, franchiseId: f.Id))
            throw new ForbiddenException("This franchise is outside your assigned scope.");
        f.DeletedAt = DateTimeOffset.UtcNow; f.UpdatedBy = command.ActorId; f.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        // Semantic audit: soft-delete of a franchise (before-state) — the interceptor would only
        // see a "franchises.updated" (DeletedAt set), which does not convey removal.
        await _audit.WriteAsync("franchise.delete", "franchises", f.Id,
            resourceDisplay: f.DisplayName ?? f.LegalName,
            oldValues: new { f.Code, f.LegalName, f.BrandId }, ct: cancellationToken);
        return true;
    }
}
