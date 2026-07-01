using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;

namespace core.Application.Identity.TenancyOrg.Stores.Commands.CreateStore;

public sealed record CreateStoreCommand(CreateStoreRequest Request, Guid? ActorId) : ICommand<StoreDto>;

public class CreateStoreCommandHandler : ICommandHandler<CreateStoreCommand, StoreDto>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public CreateStoreCommandHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<StoreDto> HandleAsync(CreateStoreCommand command, CancellationToken cancellationToken)
    {
        if (!_user.IsWithinScope(brandId: command.Request.BrandId, franchiseId: command.Request.FranchiseId))
            throw new ForbiddenException("This store is outside your assigned scope.");
        var now = DateTimeOffset.UtcNow;
        var s = new Store
        {
            Id = Guid.NewGuid(), BrandId = command.Request.BrandId, FranchiseId = command.Request.FranchiseId,
            Code = command.Request.Code, Name = command.Request.Name, StoreType = command.Request.StoreType,
            AddressLine1 = command.Request.AddressLine1, City = command.Request.City,
            State = command.Request.State, Pincode = command.Request.Pincode, CountryCode = "IN",
            Timezone = "Asia/Kolkata", CurrencyCode = "INR",
            DailyPickupCapacity = 200, DailyDeliveryCapacity = 200, SlotDurationMinutes = 120,
            AcceptsExpress = true, AcceptsCod = true, AcceptsWalkin = true,
            ServiceRadiusKm = 5, RatingCount = 0,
            Config = "{}", Status = "active",
            CreatedAt = now, UpdatedAt = now, Version = 1, CreatedBy = command.ActorId
        };
        _db.Stores.Add(s);
        await _db.SaveChangesAsync(cancellationToken);
        return new StoreDto(s.Id, s.BrandId, s.FranchiseId, s.Code, s.Name, s.StoreType, s.City, s.Status, s.CreatedAt);
    }
}
