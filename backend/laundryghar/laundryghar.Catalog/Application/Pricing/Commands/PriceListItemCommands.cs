using FluentValidation;
using laundryghar.Catalog.Application.Pricing.Dtos;
using MediatR;

namespace laundryghar.Catalog.Application.Pricing.Commands;

// ── Create PriceListItem ──────────────────────────────────────────────────────

public sealed record CreatePriceListItemCommand(
    Guid PriceListId,
    CreatePriceListItemRequest Request,
    Guid? ActorId
) : IRequest<PriceListItemDto>;

public sealed class CreatePriceListItemHandler : IRequestHandler<CreatePriceListItemCommand, PriceListItemDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreatePriceListItemHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<PriceListItemDto> Handle(CreatePriceListItemCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        // Brand predicate: verifies the price list belongs to the caller's brand before any mutation.
        var priceList = await _db.PriceLists
            .FirstOrDefaultAsync(x => x.Id == cmd.PriceListId && x.BrandId == brandId, ct);
        if (priceList is null || priceList.DeletedAt != null)
            throw new KeyNotFoundException("Price list not found.");
        if (priceList.IsPublished)
            throw new BusinessRuleException("Cannot add items to a published price list.");

        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var e = new PriceListItem
        {
            Id              = Guid.NewGuid(),
            PriceListId     = cmd.PriceListId,
            BrandId         = brandId,
            ServiceId       = req.ServiceId,
            ItemId          = req.ItemId,
            ItemVariantId   = req.ItemVariantId,
            FabricTypeId    = req.FabricTypeId,
            ItemGroupId     = req.ItemGroupId,
            BasePrice       = req.BasePrice,
            ExpressPrice    = req.ExpressPrice,
            MinimumQuantity = req.MinimumQuantity,
            TaxRatePercent  = req.TaxRatePercent,
            IsTaxable       = req.IsTaxable,
            DisplayLabel    = req.DisplayLabel,
            Notes           = req.Notes,
            IsActive        = true,
            Status          = "active",
            CreatedAt       = now,
            UpdatedAt       = now,
            CreatedBy       = cmd.ActorId,
            UpdatedBy       = cmd.ActorId
        };

        _db.PriceListItems.Add(e);
        await _db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    internal static PriceListItemDto ToDto(PriceListItem e) => new(
        e.Id, e.PriceListId, e.BrandId, e.ServiceId, e.ItemId,
        e.ItemVariantId, e.FabricTypeId, e.ItemGroupId,
        e.BasePrice, e.ExpressPrice, e.MinimumQuantity,
        e.TaxRatePercent, e.IsTaxable, e.DisplayLabel, e.Notes,
        e.IsActive, e.Status, e.CreatedAt, e.UpdatedAt);
}

// ── Update PriceListItem ──────────────────────────────────────────────────────

public sealed record UpdatePriceListItemCommand(
    Guid PriceListId,
    Guid Id,
    UpdatePriceListItemRequest Request,
    Guid? ActorId
) : IRequest<PriceListItemDto?>;

public sealed class UpdatePriceListItemHandler : IRequestHandler<UpdatePriceListItemCommand, PriceListItemDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdatePriceListItemHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PriceListItemDto?> Handle(UpdatePriceListItemCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        // Scope by brand_id to prevent cross-brand mutation.
        var e = await _db.PriceListItems
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.PriceListId == cmd.PriceListId && x.BrandId == brandId, ct);
        if (e is null) return null;

        var req = cmd.Request;
        e.BasePrice       = req.BasePrice;
        e.ExpressPrice    = req.ExpressPrice;
        e.MinimumQuantity = req.MinimumQuantity;
        e.TaxRatePercent  = req.TaxRatePercent;
        e.IsTaxable       = req.IsTaxable;
        e.DisplayLabel    = req.DisplayLabel;
        e.Notes           = req.Notes;
        e.IsActive        = req.IsActive;
        e.UpdatedAt       = DateTimeOffset.UtcNow;
        e.UpdatedBy       = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return CreatePriceListItemHandler.ToDto(e);
    }
}

public sealed class CreatePriceListItemValidator : AbstractValidator<CreatePriceListItemCommand>
{
    public CreatePriceListItemValidator()
    {
        RuleFor(x => x.Request.ServiceId).NotEmpty();
        RuleFor(x => x.Request.ItemId).NotEmpty();
        RuleFor(x => x.Request.BasePrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.TaxRatePercent).InclusiveBetween(0, 100);
    }
}
