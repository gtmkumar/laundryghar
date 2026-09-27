using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;

namespace core.Application.Identity.TenancyOrg.Franchises.Commands.CreateFranchise;

public sealed record CreateFranchiseCommand(CreateFranchiseRequest Request, Guid? ActorId) : ICommand<FranchiseDto>;

public class CreateFranchiseCommandHandler : ICommandHandler<CreateFranchiseCommand, FranchiseDto>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public CreateFranchiseCommandHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<FranchiseDto> HandleAsync(CreateFranchiseCommand command, CancellationToken cancellationToken)
    {
        // A0.7 — this was the only handler in its family with no scope guard: it did not inject
        // ICurrentUser at all and took BrandId straight off the request body. Its four siblings
        // (CreateStore.cs:25, CreateWarehouse, UpdateFranchise, DeleteFranchise) each carry exactly
        // this line.
        //
        // The franchises RLS policy has a WITH CHECK on brand_id, so a CROSS-BRAND insert was
        // already blocked at the database. What was not blocked is SUB-BRAND escalation: a
        // franchise- or store-scoped holder of `franchises.create` could mint a sibling franchise
        // inside their own brand, which is precisely the boundary IsWithinScope exists to enforce
        // and which RLS cannot express (no policy references app.current_franchise_id).
        if (!_user.IsWithinScope(brandId: command.Request.BrandId))
            throw new ForbiddenException("This franchise is outside your assigned scope.");

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
