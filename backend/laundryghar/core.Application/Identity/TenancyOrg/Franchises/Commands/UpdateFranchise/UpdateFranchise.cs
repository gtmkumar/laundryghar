using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.TenancyOrg.Franchises.Commands.UpdateFranchise;

public sealed record UpdateFranchiseCommand(Guid Id, UpdateFranchiseRequest Request, Guid? ActorId) : ICommand<FranchiseDto?>;

public class UpdateFranchiseCommandHandler : ICommandHandler<UpdateFranchiseCommand, FranchiseDto?>
{
    private readonly ICoreDbContext _db;

    public UpdateFranchiseCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<FranchiseDto?> HandleAsync(UpdateFranchiseCommand command, CancellationToken cancellationToken)
    {
        var f = await _db.Franchises.FindAsync([command.Id], cancellationToken);
        if (f is null) return null;
        if (command.Request.LegalName        is not null) f.LegalName        = command.Request.LegalName;
        if (command.Request.OnboardingStatus is not null) f.OnboardingStatus = command.Request.OnboardingStatus;
        if (command.Request.Status           is not null) f.Status           = command.Request.Status;
        f.UpdatedAt = DateTimeOffset.UtcNow; f.UpdatedBy = command.ActorId; f.Version++;
        await _db.SaveChangesAsync(cancellationToken);
        return new FranchiseDto(f.Id, f.BrandId, f.Code, f.LegalName, f.OnboardingStatus, f.Status, f.CreatedAt);
    }
}
