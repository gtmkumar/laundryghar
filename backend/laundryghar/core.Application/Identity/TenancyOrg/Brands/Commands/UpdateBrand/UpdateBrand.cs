using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.TenancyOrg.Brands.Commands.UpdateBrand;

public sealed record UpdateBrandCommand(Guid Id, UpdateBrandRequest Request, Guid? ActorId) : ICommand<BrandDto?>;

public class UpdateBrandCommandHandler : ICommandHandler<UpdateBrandCommand, BrandDto?>
{
    private readonly ICoreDbContext _db;

    public UpdateBrandCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<BrandDto?> HandleAsync(UpdateBrandCommand command, CancellationToken cancellationToken)
    {
        var brand = await _db.Brands.FindAsync([command.Id], cancellationToken);
        if (brand is null) return null;

        if (command.Request.Name         is not null) brand.Name         = command.Request.Name;
        if (command.Request.LegalName    is not null) brand.LegalName    = command.Request.LegalName;
        if (command.Request.Tagline      is not null) brand.Tagline      = command.Request.Tagline;
        if (command.Request.Status       is not null) brand.Status       = command.Request.Status;
        if (command.Request.SupportEmail is not null) brand.SupportEmail = command.Request.SupportEmail;
        if (command.Request.SupportPhone is not null) brand.SupportPhone = command.Request.SupportPhone;
        if (command.Request.LogoUrl      is not null) brand.LogoUrl      = command.Request.LogoUrl;

        brand.UpdatedAt = DateTimeOffset.UtcNow;
        brand.UpdatedBy = command.ActorId;
        brand.Version++;

        await _db.SaveChangesAsync(cancellationToken);
        return new BrandDto(brand.Id, brand.PlatformId, brand.Code, brand.Name, brand.LegalName,
            brand.Tagline, brand.CurrencyCode, brand.Timezone, brand.VerticalKey, brand.Status, brand.CreatedAt, brand.UpdatedAt);
    }
}
