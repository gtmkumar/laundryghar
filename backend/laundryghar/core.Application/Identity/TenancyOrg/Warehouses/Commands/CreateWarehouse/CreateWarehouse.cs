using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace core.Application.Identity.TenancyOrg.Warehouses.Commands.CreateWarehouse;

public sealed record CreateWarehouseCommand(CreateWarehouseRequest Request, Guid? ActorId) : ICommand<WarehouseDto>;

public class CreateWarehouseCommandHandler : ICommandHandler<CreateWarehouseCommand, WarehouseDto>
{
    private readonly ICoreDbContext _db;

    public CreateWarehouseCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<WarehouseDto> HandleAsync(CreateWarehouseCommand command, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var w = new Warehouse
        {
            Id = Guid.NewGuid(), BrandId = command.Request.BrandId, FranchiseId = command.Request.FranchiseId,
            Code = command.Request.Code, Name = command.Request.Name, WarehouseType = command.Request.WarehouseType,
            AddressLine1 = command.Request.AddressLine1, City = command.Request.City,
            State = command.Request.State, Pincode = command.Request.Pincode, CountryCode = "IN",
            Timezone = "Asia/Kolkata", DailyThroughputTarget = 1000, CurrentLoadCount = 0,
            ProcessingCapabilities = new laundryghar.SharedDataModel.Entities.TenancyOrg.WarehouseCapabilities
            {
                HasDryClean = true, HasSteamIron = true,
            },
            Capabilities = [], OperatingHoursConfig = "{}", Config = "{}", Status = "active",
            CreatedAt = now, UpdatedAt = now, Version = 1, CreatedBy = command.ActorId
        };
        _db.Warehouses.Add(w);
        await _db.SaveChangesAsync(cancellationToken);
        return new WarehouseDto(w.Id, w.BrandId, w.FranchiseId, w.Code, w.Name, w.City, w.Status, w.CreatedAt);
    }
}
