using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Identity.Application.TenancyOrg.Commands;

// ─── Platform ──────────────────────────────────────────────────────────────

public sealed record GetPlatformsQuery(int Page = 1, int PageSize = 20) : IRequest<PaginatedList<PlatformDto>>;
public sealed record GetPlatformByIdQuery(Guid Id)                       : IRequest<PlatformDto?>;
public sealed record CreatePlatformCommand(string Code, string Name, string? LegalName, string? Domain, Guid? ActorId) : IRequest<PlatformDto>;

public sealed record PlatformDto(Guid Id, string Code, string Name, string? LegalName, string Status, DateTimeOffset CreatedAt);

public sealed class GetPlatformsHandler : IRequestHandler<GetPlatformsQuery, PaginatedList<PlatformDto>>
{
    private readonly LaundryGharDbContext _db;
    public GetPlatformsHandler(LaundryGharDbContext db) => _db = db;
    public Task<PaginatedList<PlatformDto>> Handle(GetPlatformsQuery r, CancellationToken ct) =>
        PaginatedList<PlatformDto>.CreateAsync(
            _db.Platforms.AsNoTracking().OrderBy(p => p.Name)
               .Select(p => new PlatformDto(p.Id, p.Code, p.Name, p.LegalName, p.Status, p.CreatedAt)),
            r.Page, r.PageSize, ct);
}

public sealed class GetPlatformByIdHandler : IRequestHandler<GetPlatformByIdQuery, PlatformDto?>
{
    private readonly LaundryGharDbContext _db;
    public GetPlatformByIdHandler(LaundryGharDbContext db) => _db = db;
    public Task<PlatformDto?> Handle(GetPlatformByIdQuery r, CancellationToken ct) =>
        _db.Platforms.AsNoTracking()
            .Where(p => p.Id == r.Id)
            .Select(p => new PlatformDto(p.Id, p.Code, p.Name, p.LegalName, p.Status, p.CreatedAt))
            .FirstOrDefaultAsync(ct);
}

public sealed class CreatePlatformHandler : IRequestHandler<CreatePlatformCommand, PlatformDto>
{
    private readonly LaundryGharDbContext _db;
    public CreatePlatformHandler(LaundryGharDbContext db) => _db = db;
    public async Task<PlatformDto> Handle(CreatePlatformCommand cmd, CancellationToken ct)
    {
        var p = new Platform
        {
            Id = Guid.NewGuid(), Code = cmd.Code, Name = cmd.Name, LegalName = cmd.LegalName,
            Domain = cmd.Domain, Config = "{}", Status = "active",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, Version = 1, CreatedBy = cmd.ActorId
        };
        _db.Platforms.Add(p);
        await _db.SaveChangesAsync(ct);
        return new PlatformDto(p.Id, p.Code, p.Name, p.LegalName, p.Status, p.CreatedAt);
    }
}

// ─── Franchise ─────────────────────────────────────────────────────────────

public sealed record FranchiseDto(Guid Id, Guid BrandId, string Code, string LegalName, string OnboardingStatus, string Status, DateTimeOffset CreatedAt);
public sealed record CreateFranchiseRequest(Guid BrandId, string Code, string LegalName, string ContactPhone, string? ContactEmail = null, string BillingAddress = "{}");
public sealed record UpdateFranchiseRequest(string? LegalName, string? OnboardingStatus, string? Status);

public sealed record GetFranchisesQuery(Guid? BrandId, int Page = 1, int PageSize = 20) : IRequest<PaginatedList<FranchiseDto>>;
public sealed record GetFranchiseByIdQuery(Guid Id)                                      : IRequest<FranchiseDto?>;
public sealed record CreateFranchiseCommand(CreateFranchiseRequest Request, Guid? ActorId) : IRequest<FranchiseDto>;
public sealed record UpdateFranchiseCommand(Guid Id, UpdateFranchiseRequest Request, Guid? ActorId) : IRequest<FranchiseDto?>;
public sealed record DeleteFranchiseCommand(Guid Id, Guid? ActorId)                        : IRequest<bool>;

public sealed class GetFranchisesHandler : IRequestHandler<GetFranchisesQuery, PaginatedList<FranchiseDto>>
{
    private readonly LaundryGharDbContext _db;
    public GetFranchisesHandler(LaundryGharDbContext db) => _db = db;
    public Task<PaginatedList<FranchiseDto>> Handle(GetFranchisesQuery r, CancellationToken ct)
    {
        var q = _db.Franchises.AsNoTracking().AsQueryable();
        if (r.BrandId.HasValue) q = q.Where(f => f.BrandId == r.BrandId.Value);
        return PaginatedList<FranchiseDto>.CreateAsync(
            q.OrderBy(f => f.LegalName).Select(f => new FranchiseDto(f.Id, f.BrandId, f.Code, f.LegalName, f.OnboardingStatus, f.Status, f.CreatedAt)),
            r.Page, r.PageSize, ct);
    }
}

public sealed class GetFranchiseByIdHandler : IRequestHandler<GetFranchiseByIdQuery, FranchiseDto?>
{
    private readonly LaundryGharDbContext _db;
    public GetFranchiseByIdHandler(LaundryGharDbContext db) => _db = db;
    public Task<FranchiseDto?> Handle(GetFranchiseByIdQuery r, CancellationToken ct) =>
        _db.Franchises.AsNoTracking().Where(f => f.Id == r.Id)
            .Select(f => new FranchiseDto(f.Id, f.BrandId, f.Code, f.LegalName, f.OnboardingStatus, f.Status, f.CreatedAt))
            .FirstOrDefaultAsync(ct);
}

public sealed class CreateFranchiseHandler : IRequestHandler<CreateFranchiseCommand, FranchiseDto>
{
    private readonly LaundryGharDbContext _db;
    public CreateFranchiseHandler(LaundryGharDbContext db) => _db = db;
    public async Task<FranchiseDto> Handle(CreateFranchiseCommand cmd, CancellationToken ct)
    {
        var f = new Franchise
        {
            Id = Guid.NewGuid(), BrandId = cmd.Request.BrandId, Code = cmd.Request.Code,
            LegalName = cmd.Request.LegalName, ContactPhone = cmd.Request.ContactPhone,
            ContactEmail = cmd.Request.ContactEmail,
            BillingAddress = cmd.Request.BillingAddress,
            OnboardingStatus = "pending", Config = "{}", Metadata = "{}",
            Status = "active", RoyaltyPercent = 0, MarketingFeePercent = 0,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1, CreatedBy = cmd.ActorId
        };
        _db.Franchises.Add(f);
        await _db.SaveChangesAsync(ct);
        return new FranchiseDto(f.Id, f.BrandId, f.Code, f.LegalName, f.OnboardingStatus, f.Status, f.CreatedAt);
    }
}

public sealed class UpdateFranchiseHandler : IRequestHandler<UpdateFranchiseCommand, FranchiseDto?>
{
    private readonly LaundryGharDbContext _db;
    public UpdateFranchiseHandler(LaundryGharDbContext db) => _db = db;
    public async Task<FranchiseDto?> Handle(UpdateFranchiseCommand cmd, CancellationToken ct)
    {
        var f = await _db.Franchises.FindAsync([cmd.Id], ct);
        if (f is null) return null;
        if (cmd.Request.LegalName       is not null) f.LegalName       = cmd.Request.LegalName;
        if (cmd.Request.OnboardingStatus is not null) f.OnboardingStatus = cmd.Request.OnboardingStatus;
        if (cmd.Request.Status          is not null) f.Status          = cmd.Request.Status;
        f.UpdatedAt = DateTimeOffset.UtcNow; f.UpdatedBy = cmd.ActorId; f.Version++;
        await _db.SaveChangesAsync(ct);
        return new FranchiseDto(f.Id, f.BrandId, f.Code, f.LegalName, f.OnboardingStatus, f.Status, f.CreatedAt);
    }
}

public sealed class DeleteFranchiseHandler : IRequestHandler<DeleteFranchiseCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    public DeleteFranchiseHandler(LaundryGharDbContext db) => _db = db;
    public async Task<bool> Handle(DeleteFranchiseCommand cmd, CancellationToken ct)
    {
        var f = await _db.Franchises.FindAsync([cmd.Id], ct);
        if (f is null) return false;
        f.DeletedAt = DateTimeOffset.UtcNow; f.UpdatedBy = cmd.ActorId; f.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

// ─── Store ─────────────────────────────────────────────────────────────────

public sealed record StoreDto(Guid Id, Guid BrandId, Guid FranchiseId, string Code, string Name, string StoreType, string City, string Status, DateTimeOffset CreatedAt);
public sealed record CreateStoreRequest(Guid BrandId, Guid FranchiseId, string Code, string Name, string AddressLine1, string City, string State, string Pincode, string StoreType = "walkin");
public sealed record UpdateStoreRequest(string? Name, string? Status, string? ContactPhone);

public sealed record GetStoresQuery(Guid? BrandId, Guid? FranchiseId, int Page = 1, int PageSize = 20) : IRequest<PaginatedList<StoreDto>>;
public sealed record GetStoreByIdQuery(Guid Id)                                                         : IRequest<StoreDto?>;
public sealed record CreateStoreCommand(CreateStoreRequest Request, Guid? ActorId) : IRequest<StoreDto>;
public sealed record UpdateStoreCommand(Guid Id, UpdateStoreRequest Request, Guid? ActorId) : IRequest<StoreDto?>;
public sealed record DeleteStoreCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

public sealed class GetStoresHandler : IRequestHandler<GetStoresQuery, PaginatedList<StoreDto>>
{
    private readonly LaundryGharDbContext _db;
    public GetStoresHandler(LaundryGharDbContext db) => _db = db;
    public Task<PaginatedList<StoreDto>> Handle(GetStoresQuery r, CancellationToken ct)
    {
        var q = _db.Stores.AsNoTracking().AsQueryable();
        if (r.BrandId.HasValue)     q = q.Where(s => s.BrandId     == r.BrandId.Value);
        if (r.FranchiseId.HasValue) q = q.Where(s => s.FranchiseId == r.FranchiseId.Value);
        return PaginatedList<StoreDto>.CreateAsync(
            q.OrderBy(s => s.Name).Select(s => new StoreDto(s.Id, s.BrandId, s.FranchiseId, s.Code, s.Name, s.StoreType, s.City, s.Status, s.CreatedAt)),
            r.Page, r.PageSize, ct);
    }
}

public sealed class GetStoreByIdHandler : IRequestHandler<GetStoreByIdQuery, StoreDto?>
{
    private readonly LaundryGharDbContext _db;
    public GetStoreByIdHandler(LaundryGharDbContext db) => _db = db;
    public Task<StoreDto?> Handle(GetStoreByIdQuery r, CancellationToken ct) =>
        _db.Stores.AsNoTracking().Where(s => s.Id == r.Id)
            .Select(s => new StoreDto(s.Id, s.BrandId, s.FranchiseId, s.Code, s.Name, s.StoreType, s.City, s.Status, s.CreatedAt))
            .FirstOrDefaultAsync(ct);
}

public sealed class CreateStoreHandler : IRequestHandler<CreateStoreCommand, StoreDto>
{
    private readonly LaundryGharDbContext _db;
    public CreateStoreHandler(LaundryGharDbContext db) => _db = db;
    public async Task<StoreDto> Handle(CreateStoreCommand cmd, CancellationToken ct)
    {
        var s = new Store
        {
            Id = Guid.NewGuid(), BrandId = cmd.Request.BrandId, FranchiseId = cmd.Request.FranchiseId,
            Code = cmd.Request.Code, Name = cmd.Request.Name, StoreType = cmd.Request.StoreType,
            AddressLine1 = cmd.Request.AddressLine1, City = cmd.Request.City,
            State = cmd.Request.State, Pincode = cmd.Request.Pincode, CountryCode = "IN",
            Timezone = "Asia/Kolkata", CurrencyCode = "INR",
            DailyPickupCapacity = 200, DailyDeliveryCapacity = 200, SlotDurationMinutes = 120,
            AcceptsExpress = true, AcceptsCod = true, AcceptsWalkin = true,
            ServiceRadiusKm = 5, RatingCount = 0,
            Config = "{}", Status = "active",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, Version = 1, CreatedBy = cmd.ActorId
        };
        _db.Stores.Add(s);
        await _db.SaveChangesAsync(ct);
        return new StoreDto(s.Id, s.BrandId, s.FranchiseId, s.Code, s.Name, s.StoreType, s.City, s.Status, s.CreatedAt);
    }
}

public sealed class UpdateStoreHandler : IRequestHandler<UpdateStoreCommand, StoreDto?>
{
    private readonly LaundryGharDbContext _db;
    public UpdateStoreHandler(LaundryGharDbContext db) => _db = db;
    public async Task<StoreDto?> Handle(UpdateStoreCommand cmd, CancellationToken ct)
    {
        var s = await _db.Stores.FindAsync([cmd.Id], ct);
        if (s is null) return null;
        if (cmd.Request.Name         is not null) s.Name         = cmd.Request.Name;
        if (cmd.Request.Status       is not null) s.Status       = cmd.Request.Status;
        if (cmd.Request.ContactPhone is not null) s.ContactPhone = cmd.Request.ContactPhone;
        s.UpdatedAt = DateTimeOffset.UtcNow; s.UpdatedBy = cmd.ActorId; s.Version++;
        await _db.SaveChangesAsync(ct);
        return new StoreDto(s.Id, s.BrandId, s.FranchiseId, s.Code, s.Name, s.StoreType, s.City, s.Status, s.CreatedAt);
    }
}

public sealed class DeleteStoreHandler : IRequestHandler<DeleteStoreCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    public DeleteStoreHandler(LaundryGharDbContext db) => _db = db;
    public async Task<bool> Handle(DeleteStoreCommand cmd, CancellationToken ct)
    {
        var s = await _db.Stores.FindAsync([cmd.Id], ct);
        if (s is null) return false;
        s.DeletedAt = DateTimeOffset.UtcNow; s.UpdatedBy = cmd.ActorId; s.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

// ─── Warehouse ─────────────────────────────────────────────────────────────

public sealed record WarehouseDto(Guid Id, Guid BrandId, Guid FranchiseId, string Code, string Name, string City, string Status, DateTimeOffset CreatedAt);
public sealed record CreateWarehouseRequest(Guid BrandId, Guid FranchiseId, string Code, string Name, string AddressLine1, string City, string State, string Pincode, string WarehouseType = "central");
public sealed record UpdateWarehouseRequest(string? Name, string? Status, string? ContactPhone);
public sealed record GetWarehousesQuery(Guid? BrandId, Guid? FranchiseId, int Page = 1, int PageSize = 20) : IRequest<PaginatedList<WarehouseDto>>;
public sealed record CreateWarehouseCommand(CreateWarehouseRequest Request, Guid? ActorId) : IRequest<WarehouseDto>;
public sealed record UpdateWarehouseCommand(Guid Id, UpdateWarehouseRequest Request, Guid? ActorId) : IRequest<WarehouseDto?>;
public sealed record DeleteWarehouseCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

public sealed class GetWarehousesHandler : IRequestHandler<GetWarehousesQuery, PaginatedList<WarehouseDto>>
{
    private readonly LaundryGharDbContext _db;
    public GetWarehousesHandler(LaundryGharDbContext db) => _db = db;
    public Task<PaginatedList<WarehouseDto>> Handle(GetWarehousesQuery r, CancellationToken ct)
    {
        var q = _db.Warehouses.AsNoTracking().AsQueryable();
        if (r.BrandId.HasValue)     q = q.Where(w => w.BrandId     == r.BrandId.Value);
        if (r.FranchiseId.HasValue) q = q.Where(w => w.FranchiseId == r.FranchiseId.Value);
        return PaginatedList<WarehouseDto>.CreateAsync(
            q.OrderBy(w => w.Name).Select(w => new WarehouseDto(w.Id, w.BrandId, w.FranchiseId, w.Code, w.Name, w.City, w.Status, w.CreatedAt)),
            r.Page, r.PageSize, ct);
    }
}

public sealed class CreateWarehouseHandler : IRequestHandler<CreateWarehouseCommand, WarehouseDto>
{
    private readonly LaundryGharDbContext _db;
    public CreateWarehouseHandler(LaundryGharDbContext db) => _db = db;
    public async Task<WarehouseDto> Handle(CreateWarehouseCommand cmd, CancellationToken ct)
    {
        var w = new Warehouse
        {
            Id = Guid.NewGuid(), BrandId = cmd.Request.BrandId, FranchiseId = cmd.Request.FranchiseId,
            Code = cmd.Request.Code, Name = cmd.Request.Name, WarehouseType = cmd.Request.WarehouseType,
            AddressLine1 = cmd.Request.AddressLine1, City = cmd.Request.City,
            State = cmd.Request.State, Pincode = cmd.Request.Pincode, CountryCode = "IN",
            Timezone = "Asia/Kolkata", DailyThroughputTarget = 1000, CurrentLoadCount = 0,
            HasDryClean = true, HasSteamIron = true,
            Capabilities = [], OperatingHoursConfig = "{}", Config = "{}", Status = "active",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, Version = 1, CreatedBy = cmd.ActorId
        };
        _db.Warehouses.Add(w);
        await _db.SaveChangesAsync(ct);
        return new WarehouseDto(w.Id, w.BrandId, w.FranchiseId, w.Code, w.Name, w.City, w.Status, w.CreatedAt);
    }
}

public sealed class UpdateWarehouseHandler : IRequestHandler<UpdateWarehouseCommand, WarehouseDto?>
{
    private readonly LaundryGharDbContext _db;
    public UpdateWarehouseHandler(LaundryGharDbContext db) => _db = db;
    public async Task<WarehouseDto?> Handle(UpdateWarehouseCommand cmd, CancellationToken ct)
    {
        var w = await _db.Warehouses.FindAsync([cmd.Id], ct);
        if (w is null) return null;
        if (cmd.Request.Name         is not null) w.Name         = cmd.Request.Name;
        if (cmd.Request.Status       is not null) w.Status       = cmd.Request.Status;
        if (cmd.Request.ContactPhone is not null) w.ContactPhone = cmd.Request.ContactPhone;
        w.UpdatedAt = DateTimeOffset.UtcNow; w.UpdatedBy = cmd.ActorId; w.Version++;
        await _db.SaveChangesAsync(ct);
        return new WarehouseDto(w.Id, w.BrandId, w.FranchiseId, w.Code, w.Name, w.City, w.Status, w.CreatedAt);
    }
}

public sealed class DeleteWarehouseHandler : IRequestHandler<DeleteWarehouseCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    public DeleteWarehouseHandler(LaundryGharDbContext db) => _db = db;
    public async Task<bool> Handle(DeleteWarehouseCommand cmd, CancellationToken ct)
    {
        var w = await _db.Warehouses.FindAsync([cmd.Id], ct);
        if (w is null) return false;
        w.DeletedAt = DateTimeOffset.UtcNow; w.UpdatedBy = cmd.ActorId; w.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
