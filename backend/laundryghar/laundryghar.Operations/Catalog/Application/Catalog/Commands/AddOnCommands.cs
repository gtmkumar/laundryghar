using laundryghar.Catalog.Infrastructure.Auth;
using laundryghar.Catalog.Infrastructure.Services;
using FluentValidation;
using laundryghar.Catalog.Application.Catalog.Dtos;
using MediatR;

namespace laundryghar.Catalog.Application.Catalog.Commands;

public sealed record CreateAddOnCommand(CreateAddOnRequest Request, Guid? ActorId) : IRequest<AddOnDto>;

public sealed class CreateAddOnHandler : IRequestHandler<CreateAddOnCommand, AddOnDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateAddOnHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<AddOnDto> Handle(CreateAddOnCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var e = new AddOn
        {
            Id                    = Guid.NewGuid(),
            BrandId               = brandId,
            Code                  = req.Code,
            Name                  = req.Name,
            NameLocalized         = req.NameLocalized,
            Description           = req.Description,
            PricingType           = req.PricingType,
            PriceValue            = req.PriceValue,
            MinCharge             = req.MinCharge,
            MaxCharge             = req.MaxCharge,
            ApplicableServices    = req.ApplicableServices ?? [],
            ApplicableCategories  = req.ApplicableCategories ?? [],
            IsTaxable             = req.IsTaxable,
            TaxRatePercent        = req.TaxRatePercent,
            RequiresApproval      = req.RequiresApproval,
            IconUrl               = req.IconUrl,
            DisplayOrder          = req.DisplayOrder,
            Status                = "active",
            CreatedAt             = now,
            UpdatedAt             = now,
            CreatedBy             = cmd.ActorId,
            UpdatedBy             = cmd.ActorId
        };

        _db.AddOns.Add(e);
        await _db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    internal static AddOnDto ToDto(AddOn e) => new(
        e.Id, e.BrandId, e.Code, e.Name, e.NameLocalized, e.Description,
        e.PricingType, e.PriceValue, e.MinCharge, e.MaxCharge,
        e.ApplicableServices, e.ApplicableCategories,
        e.IsTaxable, e.TaxRatePercent, e.RequiresApproval,
        e.IconUrl, e.DisplayOrder, e.Status, e.CreatedAt, e.UpdatedAt);
}

public sealed record UpdateAddOnCommand(Guid Id, UpdateAddOnRequest Request, Guid? ActorId) : IRequest<AddOnDto?>;

public sealed class UpdateAddOnHandler : IRequestHandler<UpdateAddOnCommand, AddOnDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateAddOnHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<AddOnDto?> Handle(UpdateAddOnCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.AddOns
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return null;

        var req = cmd.Request;
        e.Name                 = req.Name;
        e.NameLocalized        = req.NameLocalized;
        e.Description          = req.Description;
        e.PricingType          = req.PricingType;
        e.PriceValue           = req.PriceValue;
        e.MinCharge            = req.MinCharge;
        e.MaxCharge            = req.MaxCharge;
        e.ApplicableServices   = req.ApplicableServices ?? [];
        e.ApplicableCategories = req.ApplicableCategories ?? [];
        e.IsTaxable            = req.IsTaxable;
        e.TaxRatePercent       = req.TaxRatePercent;
        e.RequiresApproval     = req.RequiresApproval;
        e.IconUrl              = req.IconUrl;
        e.DisplayOrder         = req.DisplayOrder;
        e.Status               = req.Status;
        e.UpdatedAt            = DateTimeOffset.UtcNow;
        e.UpdatedBy            = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return CreateAddOnHandler.ToDto(e);
    }
}

public sealed record DeleteAddOnCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

public sealed class DeleteAddOnHandler : IRequestHandler<DeleteAddOnCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteAddOnHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> Handle(DeleteAddOnCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.AddOns
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return false;

        // Soft-delete must also move status off 'active' so status-keyed reports don't
        // miscount it. add_ons CHECK is ('active','disabled') — 'disabled' is terminal.
        e.Status    = "disabled";
        e.DeletedAt = DateTimeOffset.UtcNow;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class CreateAddOnValidator : AbstractValidator<CreateAddOnCommand>
{
    public CreateAddOnValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.NameLocalized).NotEmpty().MaximumLength(200).MustBeJsonObject();
        RuleFor(x => x.Request.PricingType).NotEmpty();
        RuleFor(x => x.Request.PriceValue).GreaterThanOrEqualTo(0);
    }
}
