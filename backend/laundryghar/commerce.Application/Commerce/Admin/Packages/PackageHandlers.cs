using commerce.Application.Common.Interfaces;
using FluentValidation;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Commerce.Admin.Packages;

// ── Queries ───────────────────────────────────────────────────────────────────

public sealed record GetPackagesQuery(int Page, int PageSize) : IQuery<PaginatedList<PackageDto>>;

public sealed class GetPackagesHandler : IQueryHandler<GetPackagesQuery, PaginatedList<PackageDto>>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public GetPackagesHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<PackageDto>> HandleAsync(GetPackagesQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query = _db.Packages
            .Where(x => x.BrandId == brandId && x.DeletedAt == null)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => ToDto(x));
        return PaginatedList<PackageDto>.CreateAsync(query, q.Page, q.PageSize, ct);
    }

    internal static PackageDto ToDto(Package x) => new(
        x.Id, x.BrandId, x.Code, x.Name, x.NameLocalized, x.Tier, x.Description,
        x.Price, x.CreditValue, x.DiscountPercent, x.CreditMultiplier,
        x.ValidityDays, x.IsUnlimitedValidity, x.ApplicableServices, x.ExcludedServices,
        x.MinimumOrderValue, x.MaxUsagePerOrder, x.MaxPurchasesPerCust,
        x.IconUrl, x.ColorHex, x.DisplayOrder, x.IsFeatured, x.TermsAndConditions,
        x.Status, x.AvailableFrom, x.AvailableTo, x.CreatedAt, x.UpdatedAt);
}

public sealed record GetPackageByIdQuery(Guid Id) : IQuery<PackageDto?>;

public sealed class GetPackageByIdHandler : IQueryHandler<GetPackageByIdQuery, PackageDto?>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public GetPackageByIdHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PackageDto?> HandleAsync(GetPackageByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Packages.FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId && x.DeletedAt == null, ct);
        return e is null ? null : GetPackagesHandler.ToDto(e);
    }
}

// ── Commands ──────────────────────────────────────────────────────────────────

public sealed record CreatePackageCommand(CreatePackageRequest Request, Guid? ActorId) : ICommand<PackageDto>;

public sealed class CreatePackageHandler : ICommandHandler<CreatePackageCommand, PackageDto>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public CreatePackageHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PackageDto> HandleAsync(CreatePackageCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var entity = new Package
        {
            Id                  = Guid.NewGuid(),
            BrandId             = brandId,
            Code                = req.Code,
            Name                = req.Name,
            NameLocalized       = req.NameLocalized,
            Tier                = req.Tier,
            Description         = req.Description,
            Price               = req.Price,
            CreditValue         = req.CreditValue,
            DiscountPercent     = req.DiscountPercent,
            CreditMultiplier    = req.CreditMultiplier,
            ValidityDays        = req.ValidityDays,
            IsUnlimitedValidity = req.IsUnlimitedValidity,
            ApplicableServices  = req.ApplicableServices ?? [],
            ExcludedServices    = req.ExcludedServices ?? [],
            MinimumOrderValue   = req.MinimumOrderValue,
            MaxUsagePerOrder    = req.MaxUsagePerOrder,
            MaxPurchasesPerCust = req.MaxPurchasesPerCust,
            IconUrl             = req.IconUrl,
            ColorHex            = req.ColorHex,
            DisplayOrder        = req.DisplayOrder,
            IsFeatured          = req.IsFeatured,
            TermsAndConditions  = req.TermsAndConditions,
            Status              = "active",
            AvailableFrom       = req.AvailableFrom,
            AvailableTo         = req.AvailableTo,
            CreatedAt           = now,
            UpdatedAt           = now,
            CreatedBy           = cmd.ActorId,
            UpdatedBy           = cmd.ActorId,
            Version             = 1
        };

        _db.Packages.Add(entity);
        await _db.SaveChangesAsync(ct);
        return GetPackagesHandler.ToDto(entity);
    }
}

public sealed class CreatePackageValidator : AbstractValidator<CreatePackageRequest>
{
    public CreatePackageValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NameLocalized).NotEmpty().MustBeJsonObject();
        RuleFor(x => x.Tier).NotEmpty();
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.CreditValue).GreaterThan(0);
        RuleFor(x => x.CreditMultiplier).GreaterThan(0);
    }
}

public sealed record UpdatePackageCommand(Guid Id, UpdatePackageRequest Request, Guid? ActorId) : ICommand<PackageDto?>;

public sealed class UpdatePackageHandler : ICommandHandler<UpdatePackageCommand, PackageDto?>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public UpdatePackageHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PackageDto?> HandleAsync(UpdatePackageCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.Packages.FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId && x.DeletedAt == null, ct);
        if (entity is null) return null;

        var req = cmd.Request;
        entity.Name                = req.Name;
        entity.NameLocalized       = req.NameLocalized;
        entity.Description         = req.Description;
        entity.Price               = req.Price;
        entity.CreditValue         = req.CreditValue;
        entity.DiscountPercent     = req.DiscountPercent;
        entity.CreditMultiplier    = req.CreditMultiplier;
        entity.ValidityDays        = req.ValidityDays;
        entity.IsUnlimitedValidity = req.IsUnlimitedValidity;
        entity.ApplicableServices  = req.ApplicableServices ?? [];
        entity.ExcludedServices    = req.ExcludedServices ?? [];
        entity.MinimumOrderValue   = req.MinimumOrderValue;
        entity.MaxUsagePerOrder    = req.MaxUsagePerOrder;
        entity.MaxPurchasesPerCust = req.MaxPurchasesPerCust;
        entity.IconUrl             = req.IconUrl;
        entity.ColorHex            = req.ColorHex;
        entity.DisplayOrder        = req.DisplayOrder;
        entity.IsFeatured          = req.IsFeatured;
        entity.TermsAndConditions  = req.TermsAndConditions;
        entity.AvailableFrom       = req.AvailableFrom;
        entity.AvailableTo         = req.AvailableTo;
        entity.Status              = req.Status;
        entity.UpdatedAt           = DateTimeOffset.UtcNow;
        entity.UpdatedBy           = cmd.ActorId;
        entity.Version++;

        await _db.SaveChangesAsync(ct);
        return GetPackagesHandler.ToDto(entity);
    }
}

public sealed record DeletePackageCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public sealed class DeletePackageHandler : ICommandHandler<DeletePackageCommand, bool>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public DeletePackageHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> HandleAsync(DeletePackageCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.Packages.FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId && x.DeletedAt == null, ct);
        if (entity is null) return false;

        // Soft-delete must also move status off 'active' so status-keyed reports don't
        // miscount archived packages. The packages CHECK constraint has no 'archived'
        // value; 'retired' is its terminal/archived state.
        entity.Status    = "retired";
        entity.DeletedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
