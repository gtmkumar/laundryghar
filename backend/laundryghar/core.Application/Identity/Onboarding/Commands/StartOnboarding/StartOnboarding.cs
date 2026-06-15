using core.Application.Common.Interfaces;
using core.Application.Identity.Onboarding.Dtos;
using core.Application.Identity.Onboarding.Queries.GetOnboardingState;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;

namespace core.Application.Identity.Onboarding.Commands.StartOnboarding;

// Start: create a draft franchise in 'setup'.
public sealed record StartOnboardingCommand(StartOnboardingRequest Request) : ICommand<OnboardingStateDto>;

public class StartOnboardingCommandHandler : ICommandHandler<StartOnboardingCommand, OnboardingStateDto>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _actor;

    public StartOnboardingCommandHandler(ICoreDbContext db, ICurrentUser actor) { _db = db; _actor = actor; }

    public async Task<OnboardingStateDto> HandleAsync(StartOnboardingCommand command, CancellationToken cancellationToken)
    {
        var r = command.Request;
        if (string.IsNullOrWhiteSpace(r.LegalName))
            throw new ValidationException(new Dictionary<string, string[]> { ["legalName"] = ["Legal name is required."] });
        if (string.IsNullOrWhiteSpace(r.ContactPhone))
            throw new ValidationException(new Dictionary<string, string[]> { ["contactPhone"] = ["Contact phone is required."] });

        var brandId = await OnboardingHelpers.ResolveBrandIdAsync(_actor, _db, cancellationToken)
            ?? throw new ValidationException(new Dictionary<string, string[]> { ["brand"] = ["No brand available."] });

        var now = DateTimeOffset.UtcNow;
        var f = new Franchise
        {
            Id = Guid.NewGuid(), BrandId = brandId,
            Code = await OnboardingHelpers.UniqueFranchiseCodeAsync(_db, brandId, cancellationToken),
            LegalName = r.LegalName.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(r.DisplayName) ? null : r.DisplayName.Trim(),
            ContactPhone = r.ContactPhone.Trim(),
            ContactEmail = string.IsNullOrWhiteSpace(r.ContactEmail) ? null : r.ContactEmail.Trim(),
            BillingAddress = "{}", Config = "{}", Metadata = "{}",
            RoyaltyPercent = 0, MarketingFeePercent = 0,
            OnboardingStatus = "setup", Status = "active", Version = 1,
            CreatedAt = now, UpdatedAt = now, CreatedBy = _actor.UserId, UpdatedBy = _actor.UserId,
        };
        _db.Franchises.Add(f);
        await _db.SaveChangesAsync(cancellationToken);
        return await OnboardingState.BuildAsync(_db, f, cancellationToken);
    }
}
