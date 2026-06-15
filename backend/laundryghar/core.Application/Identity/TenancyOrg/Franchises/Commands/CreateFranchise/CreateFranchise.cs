using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace core.Application.Identity.TenancyOrg.Franchises.Commands.CreateFranchise;

public sealed record CreateFranchiseCommand(CreateFranchiseRequest Request, Guid? ActorId) : ICommand<FranchiseDto>;

public class CreateFranchiseCommandHandler : ICommandHandler<CreateFranchiseCommand, FranchiseDto>
{
    private readonly ICoreDbContext _db;

    public CreateFranchiseCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<FranchiseDto> HandleAsync(CreateFranchiseCommand command, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var f = new Franchise
        {
            Id = Guid.NewGuid(), BrandId = command.Request.BrandId, Code = command.Request.Code,
            LegalName = command.Request.LegalName, ContactPhone = command.Request.ContactPhone,
            ContactEmail = command.Request.ContactEmail,
            BillingAddress = command.Request.BillingAddress,
            OnboardingStatus = "pending", Config = "{}", Metadata = "{}",
            Status = "active", RoyaltyPercent = 0, MarketingFeePercent = 0,
            CreatedAt = now, UpdatedAt = now,
            Version = 1, CreatedBy = command.ActorId
        };
        _db.Franchises.Add(f);
        await _db.SaveChangesAsync(cancellationToken);
        return new FranchiseDto(f.Id, f.BrandId, f.Code, f.LegalName, f.OnboardingStatus, f.Status, f.CreatedAt);
    }
}
