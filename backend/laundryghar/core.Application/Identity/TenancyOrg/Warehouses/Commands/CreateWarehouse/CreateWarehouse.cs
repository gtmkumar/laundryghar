using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace core.Application.Identity.TenancyOrg.Warehouses.Commands.CreateWarehouse;

public sealed record CreateWarehouseCommand(CreateWarehouseRequest Request, Guid? ActorId) : ICommand<WarehouseDto>;

public class CreateWarehouseCommandHandler : ICommandHandler<CreateWarehouseCommand, WarehouseDto>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public CreateWarehouseCommandHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<WarehouseDto> HandleAsync(CreateWarehouseCommand command, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // §6 scope boundary (docs/rbac.md): brand-level RLS does not stop a franchise-scoped
        // operator from creating a warehouse under another franchise within the same brand.
        // Enforce ancestor-or-self against the caller-supplied target; platform/brand actors pass.
        if (!_user.IsWithinScope(brandId: command.Request.BrandId, franchiseId: command.Request.FranchiseId))
            throw new ForbiddenException("This warehouse is outside your assigned scope.");

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
