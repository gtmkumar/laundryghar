using core.Application.Common.Interfaces;
using core.Application.Identity.Onboarding.Dtos;
using core.Application.Identity.Onboarding.Queries.GetOnboardingState;
using core.Application.Identity.TenancyOrg.Dtos;
using core.Application.Identity.TenancyOrg.Stores.Commands.CreateStore;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Onboarding.Commands.AddStore;

// Step 4: add a store. Delegates store creation to the Stores slice's CreateStoreCommand.
public sealed record AddStoreCommand(Guid FranchiseId, AddStoreRequest Request) : ICommand<OnboardingStateDto?>;

public class AddStoreCommandHandler : ICommandHandler<AddStoreCommand, OnboardingStateDto?>
{
    private readonly ICoreDbContext _db;
    private readonly IDispatcher _dispatcher;
    private readonly ICurrentUser _actor;

    public AddStoreCommandHandler(ICoreDbContext db, IDispatcher dispatcher, ICurrentUser actor)
    { _db = db; _dispatcher = dispatcher; _actor = actor; }

    public async Task<OnboardingStateDto?> HandleAsync(AddStoreCommand command, CancellationToken cancellationToken)
    {
        var f = await _db.Franchises.AsNoTracking().FirstOrDefaultAsync(x => x.Id == command.FranchiseId && x.DeletedAt == null, cancellationToken);
        if (f is null) return null;
        var r = command.Request;
        if (string.IsNullOrWhiteSpace(r.Name))
            throw new ValidationException(new Dictionary<string, string[]> { ["name"] = ["Store name is required."] });

        var count = await _db.Stores.AsNoTracking().CountAsync(s => s.FranchiseId == f.Id, cancellationToken);
        var code = string.IsNullOrWhiteSpace(r.Code) ? $"{f.Code}-S{count + 1:00}" : r.Code.Trim().ToUpperInvariant();

        await _dispatcher.SendAsync(new CreateStoreCommand(
            new CreateStoreRequest(f.BrandId, f.Id, code, r.Name.Trim(),
                r.AddressLine1, r.City, r.State, r.Pincode), _actor.UserId), cancellationToken);

        var fresh = await _db.Franchises.AsNoTracking().FirstAsync(x => x.Id == f.Id, cancellationToken);
        return await OnboardingState.BuildAsync(_db, fresh, cancellationToken);
    }
}
