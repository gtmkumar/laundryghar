using laundryghar.Identity.Application.TenancyOrg.Dtos;
using laundryghar.Identity.Infrastructure.Services;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Identity.Application.TenancyOrg.Commands;

// ─── Queries ──────────────────────────────────────────────────────────────

public sealed record GetBrandsQuery(BrandListParams Params) : IRequest<PaginatedList<BrandDto>>;
public sealed record GetBrandByIdQuery(Guid Id)             : IRequest<BrandDto?>;

// ─── Commands ─────────────────────────────────────────────────────────────

public sealed record CreateBrandCommand(CreateBrandRequest Request, Guid? ActorId) : IRequest<BrandDto>;
public sealed record UpdateBrandCommand(Guid Id, UpdateBrandRequest Request, Guid? ActorId) : IRequest<BrandDto?>;
public sealed record DeleteBrandCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

// ─── Handlers ─────────────────────────────────────────────────────────────

public sealed class GetBrandsHandler : IRequestHandler<GetBrandsQuery, PaginatedList<BrandDto>>
{
    private readonly LaundryGharDbContext _db;
    public GetBrandsHandler(LaundryGharDbContext db) => _db = db;

    public Task<PaginatedList<BrandDto>> Handle(GetBrandsQuery request, CancellationToken ct)
    {
        var q = _db.Brands.AsNoTracking();

        if (!string.IsNullOrEmpty(request.Params.Status))
            q = q.Where(b => b.Status == request.Params.Status);
        if (!string.IsNullOrEmpty(request.Params.Search))
            q = q.Where(b => b.Name.Contains(request.Params.Search)
                           || b.Code.Contains(request.Params.Search));

        var projected = q.OrderBy(b => b.Name).Select(b => new BrandDto(
            b.Id, b.PlatformId, b.Code, b.Name, b.LegalName, b.Tagline,
            b.CurrencyCode, b.Timezone, b.Status, b.CreatedAt, b.UpdatedAt));

        return PaginatedList<BrandDto>.CreateAsync(projected, request.Params.Page, request.Params.PageSize, ct);
    }
}

public sealed class GetBrandByIdHandler : IRequestHandler<GetBrandByIdQuery, BrandDto?>
{
    private readonly LaundryGharDbContext _db;
    public GetBrandByIdHandler(LaundryGharDbContext db) => _db = db;

    public Task<BrandDto?> Handle(GetBrandByIdQuery request, CancellationToken ct) =>
        _db.Brands.AsNoTracking()
            .Where(b => b.Id == request.Id)
            .Select(b => new BrandDto(b.Id, b.PlatformId, b.Code, b.Name, b.LegalName, b.Tagline,
                b.CurrencyCode, b.Timezone, b.Status, b.CreatedAt, b.UpdatedAt))
            .FirstOrDefaultAsync(ct);
}

public sealed class CreateBrandHandler : IRequestHandler<CreateBrandCommand, BrandDto>
{
    private readonly LaundryGharDbContext _db;
    public CreateBrandHandler(LaundryGharDbContext db) => _db = db;

    public async Task<BrandDto> Handle(CreateBrandCommand cmd, CancellationToken ct)
    {
        if (await _db.Brands.AnyAsync(b => b.Code == cmd.Request.Code, ct))
            throw new laundryghar.Utilities.Exceptions.ValidationException(
                new Dictionary<string, string[]> { ["code"] = ["Brand code already exists."] });

        var brand = new Brand
        {
            Id             = Guid.NewGuid(),
            PlatformId     = cmd.Request.PlatformId,
            Code           = cmd.Request.Code,
            Name           = cmd.Request.Name,
            LegalName      = cmd.Request.LegalName,
            Tagline        = cmd.Request.Tagline,
            CurrencyCode   = cmd.Request.CurrencyCode,
            CountryCode    = cmd.Request.CountryCode,
            Timezone       = cmd.Request.Timezone,
            LocaleDefault  = cmd.Request.LocaleDefault,
            LocalesEnabled = ["en-IN", "hi-IN"],
            Config         = "{}",
            Status         = "active",
            CreatedAt      = DateTimeOffset.UtcNow,
            UpdatedAt      = DateTimeOffset.UtcNow,
            CreatedBy      = cmd.ActorId,
            Version        = 1
        };

        _db.Brands.Add(brand);
        await _db.SaveChangesAsync(ct);

        return new BrandDto(brand.Id, brand.PlatformId, brand.Code, brand.Name, brand.LegalName,
            brand.Tagline, brand.CurrencyCode, brand.Timezone, brand.Status, brand.CreatedAt, brand.UpdatedAt);
    }
}

public sealed class UpdateBrandHandler : IRequestHandler<UpdateBrandCommand, BrandDto?>
{
    private readonly LaundryGharDbContext _db;
    public UpdateBrandHandler(LaundryGharDbContext db) => _db = db;

    public async Task<BrandDto?> Handle(UpdateBrandCommand cmd, CancellationToken ct)
    {
        var brand = await _db.Brands.FindAsync([cmd.Id], ct);
        if (brand is null) return null;

        if (cmd.Request.Name      is not null) brand.Name       = cmd.Request.Name;
        if (cmd.Request.LegalName is not null) brand.LegalName  = cmd.Request.LegalName;
        if (cmd.Request.Tagline   is not null) brand.Tagline    = cmd.Request.Tagline;
        if (cmd.Request.Status    is not null) brand.Status     = cmd.Request.Status;
        if (cmd.Request.SupportEmail is not null) brand.SupportEmail = cmd.Request.SupportEmail;
        if (cmd.Request.SupportPhone is not null) brand.SupportPhone = cmd.Request.SupportPhone;
        if (cmd.Request.LogoUrl   is not null) brand.LogoUrl    = cmd.Request.LogoUrl;

        brand.UpdatedAt = DateTimeOffset.UtcNow;
        brand.UpdatedBy = cmd.ActorId;
        brand.Version++;

        await _db.SaveChangesAsync(ct);
        return new BrandDto(brand.Id, brand.PlatformId, brand.Code, brand.Name, brand.LegalName,
            brand.Tagline, brand.CurrencyCode, brand.Timezone, brand.Status, brand.CreatedAt, brand.UpdatedAt);
    }
}

public sealed class DeleteBrandHandler : IRequestHandler<DeleteBrandCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    public DeleteBrandHandler(LaundryGharDbContext db) => _db = db;

    public async Task<bool> Handle(DeleteBrandCommand cmd, CancellationToken ct)
    {
        var brand = await _db.Brands.FindAsync([cmd.Id], ct);
        if (brand is null) return false;
        brand.DeletedAt = DateTimeOffset.UtcNow;
        brand.UpdatedBy = cmd.ActorId;
        brand.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
