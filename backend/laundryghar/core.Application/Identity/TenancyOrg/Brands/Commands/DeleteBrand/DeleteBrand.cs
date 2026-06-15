using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.TenancyOrg.Brands.Commands.DeleteBrand;

public sealed record DeleteBrandCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public class DeleteBrandCommandHandler : ICommandHandler<DeleteBrandCommand, bool>
{
    private readonly ICoreDbContext _db;

    public DeleteBrandCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<bool> HandleAsync(DeleteBrandCommand command, CancellationToken cancellationToken)
    {
        var brand = await _db.Brands.FindAsync([command.Id], cancellationToken);
        if (brand is null) return false;

        brand.DeletedAt = DateTimeOffset.UtcNow;
        brand.UpdatedBy = command.ActorId;
        brand.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
