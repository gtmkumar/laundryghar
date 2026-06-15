using core.Application.Common.Interfaces;
using core.Application.Identity.Onboarding.Dtos;
using core.Application.Identity.Onboarding.Queries.GetOnboardingState;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Onboarding.Commands.ActivateFranchise;

// Go-live: activate (gated on the checklist).
public sealed record ActivateFranchiseCommand(Guid FranchiseId) : ICommand<OnboardingStateDto?>;

public class ActivateFranchiseCommandHandler : ICommandHandler<ActivateFranchiseCommand, OnboardingStateDto?>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _actor;

    public ActivateFranchiseCommandHandler(ICoreDbContext db, ICurrentUser actor) { _db = db; _actor = actor; }

    public async Task<OnboardingStateDto?> HandleAsync(ActivateFranchiseCommand command, CancellationToken cancellationToken)
    {
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == command.FranchiseId && x.DeletedAt == null, cancellationToken);
        if (f is null) return null;

        var state = await OnboardingState.BuildAsync(_db, f, cancellationToken);
        if (state.IsActive) return state;
        if (!state.CanActivate)
        {
            var pending = string.Join(", ", state.Steps.Where(s => !s.Done).Select(s => s.Title));
            throw new ValidationException(new Dictionary<string, string[]>
                { ["onboarding"] = [$"Complete all steps before going live. Pending: {pending}."] });
        }

        var now = DateTimeOffset.UtcNow;
        f.OnboardingStatus = "active";
        f.OnboardedAt = now;
        f.UpdatedAt = now; f.UpdatedBy = _actor.UserId; f.Version++;

        // Mark the agreement signed/active at go-live.
        if (f.FranchiseAgreementId is Guid aid &&
            await _db.FranchiseAgreements.FirstOrDefaultAsync(a => a.Id == aid, cancellationToken) is { } agreement)
        {
            agreement.Status = "active";
            agreement.SignedAt ??= now;
            agreement.UpdatedAt = now; agreement.UpdatedBy = _actor.UserId; agreement.Version++;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await OnboardingState.BuildAsync(_db, f, cancellationToken);
    }
}
