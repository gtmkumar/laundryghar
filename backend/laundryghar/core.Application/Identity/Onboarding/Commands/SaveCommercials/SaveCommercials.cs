using core.Application.Common.Interfaces;
using core.Application.Identity.Onboarding.Dtos;
using core.Application.Identity.Onboarding.Queries.GetOnboardingState;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Onboarding.Commands.SaveCommercials;

// Step 2: commercials + agreement.
public sealed record SaveCommercialsCommand(Guid FranchiseId, SaveCommercialsRequest Request) : ICommand<OnboardingStateDto?>;

public class SaveCommercialsCommandHandler : ICommandHandler<SaveCommercialsCommand, OnboardingStateDto?>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _actor;

    public SaveCommercialsCommandHandler(ICoreDbContext db, ICurrentUser actor) { _db = db; _actor = actor; }

    public async Task<OnboardingStateDto?> HandleAsync(SaveCommercialsCommand command, CancellationToken cancellationToken)
    {
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == command.FranchiseId && x.DeletedAt == null, cancellationToken);
        if (f is null) return null;
        var r = command.Request;
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var term = r.TermYears <= 0 ? (short)5 : (short)r.TermYears;

        f.RoyaltyPercent = r.RoyaltyPercent;
        f.MarketingFeePercent = r.MarketingFeePercent;

        FranchiseAgreement agreement;
        if (f.FranchiseAgreementId is Guid aid &&
            await _db.FranchiseAgreements.FirstOrDefaultAsync(a => a.Id == aid, cancellationToken) is { } existing)
        {
            agreement = existing;
            agreement.RoyaltyPercent = r.RoyaltyPercent;
            agreement.MarketingFeePercent = r.MarketingFeePercent;
            agreement.InitialFranchiseFee = r.InitialFranchiseFee;
            agreement.TermYears = term;
            agreement.EffectiveTo = today.AddYears(term);
            agreement.UpdatedAt = now; agreement.UpdatedBy = _actor.UserId; agreement.Version++;
        }
        else
        {
            agreement = new FranchiseAgreement
            {
                Id = Guid.NewGuid(), BrandId = f.BrandId,
                AgreementNumber = $"AGR-{f.Code}-{today.Year}",
                AgreementType = "unit",
                FranchiseeLegalName = f.LegalName,
                FranchiseePan = f.Pan, FranchiseeGstin = f.Gstin, FranchiseePhone = f.ContactPhone, FranchiseeEmail = f.ContactEmail,
                InitialFranchiseFee = r.InitialFranchiseFee,
                RoyaltyPercent = r.RoyaltyPercent, MarketingFeePercent = r.MarketingFeePercent, TechnologyFeeMonthly = 0,
                TermYears = term, RenewalOption = true, ExclusivityClause = true, MinimumStores = 1,
                SlaTerms = "{}", EffectiveFrom = today, EffectiveTo = today.AddYears(term),
                Status = "draft", Version = 1,
                CreatedAt = now, UpdatedAt = now, CreatedBy = _actor.UserId, UpdatedBy = _actor.UserId,
            };
            _db.FranchiseAgreements.Add(agreement);
            f.FranchiseAgreementId = agreement.Id;
        }

        f.UpdatedAt = now; f.UpdatedBy = _actor.UserId; f.Version++;
        await _db.SaveChangesAsync(cancellationToken);
        return await OnboardingState.BuildAsync(_db, f, cancellationToken);
    }
}
