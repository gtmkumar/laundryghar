using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.TenancyOrg.Brands.Commands.CreateBrand;

public sealed record CreateBrandCommand(CreateBrandRequest Request, Guid? ActorId) : ICommand<BrandDto>;

public class CreateBrandCommandHandler : ICommandHandler<CreateBrandCommand, BrandDto>
{
    private readonly ICoreDbContext _db;

    public CreateBrandCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<BrandDto> HandleAsync(CreateBrandCommand command, CancellationToken cancellationToken)
    {
        if (await _db.Brands.AnyAsync(b => b.Code == command.Request.Code, cancellationToken))
            throw new laundryghar.Utilities.Exceptions.ValidationException(
                new Dictionary<string, string[]> { ["code"] = ["Brand code already exists."] });

        var now = DateTimeOffset.UtcNow;
        var brand = new Brand
        {
            Id             = Guid.NewGuid(),
            PlatformId     = command.Request.PlatformId,
            Code           = command.Request.Code,
            Name           = command.Request.Name,
            LegalName      = command.Request.LegalName,
            Tagline        = command.Request.Tagline,
            CurrencyCode   = command.Request.CurrencyCode,
            CountryCode    = command.Request.CountryCode,
            Timezone       = command.Request.Timezone,
            LocaleDefault  = command.Request.LocaleDefault,
            LocalesEnabled = ["en-IN", "hi-IN"],
            Config         = "{}",
            Status         = "active",
            CreatedAt      = now,
            UpdatedAt      = now,
            CreatedBy      = command.ActorId,
            Version        = 1
        };

        _db.Brands.Add(brand);
        await _db.SaveChangesAsync(cancellationToken);

        return new BrandDto(brand.Id, brand.PlatformId, brand.Code, brand.Name, brand.LegalName,
            brand.Tagline, brand.CurrencyCode, brand.Timezone, brand.VerticalKey, brand.Status, brand.CreatedAt, brand.UpdatedAt);
    }
}
