using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Pricing.Dtos;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Pricing.Commands.PriceListItem;

// ── Create PriceListItem ──────────────────────────────────────────────────────

public sealed record CreatePriceListItemCommand(
    Guid PriceListId,
    CreatePriceListItemRequest Request,
    Guid? ActorId
) : ICommand<PriceListItemDto>;

public sealed class CreatePriceListItemHandler : ICommandHandler<CreatePriceListItemCommand, PriceListItemDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreatePriceListItemHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<PriceListItemDto> HandleAsync(CreatePriceListItemCommand cmd, CancellationToken ct)
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

        var e = new laundryghar.SharedDataModel.Entities.CustomerCatalog.PriceListItem
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

    internal static PriceListItemDto ToDto(laundryghar.SharedDataModel.Entities.CustomerCatalog.PriceListItem e) => new(
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
) : ICommand<PriceListItemDto?>;

public sealed class UpdatePriceListItemHandler : ICommandHandler<UpdatePriceListItemCommand, PriceListItemDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public UpdatePriceListItemHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PriceListItemDto?> HandleAsync(UpdatePriceListItemCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        // Scope by brand_id to prevent cross-brand mutation.
        var e = await _db.PriceListItems
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.PriceListId == cmd.PriceListId && x.BrandId == brandId, ct);
        if (e is null) return null;

        var before = Snapshot(e);
        var oldBase = e.BasePrice;
        var label = string.IsNullOrWhiteSpace(e.DisplayLabel) ? "Price row" : e.DisplayLabel!;

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

        var summary = oldBase != e.BasePrice
            ? $"{label}: base ₹{oldBase:0.##} → ₹{e.BasePrice:0.##}"
            : $"Updated price row “{label}”";
        operations.Application.Catalog.Pricing.Common.PricingChangeLogger.Add(
            _db, brandId, "price_list_item", e.Id, summary, before, Snapshot(e), cmd.ActorId, _user.Email);

        await _db.SaveChangesAsync(ct);
        return CreatePriceListItemHandler.ToDto(e);
    }

    internal static object Snapshot(laundryghar.SharedDataModel.Entities.CustomerCatalog.PriceListItem e) => new
    {
        e.BasePrice, e.ExpressPrice, e.MinimumQuantity, e.TaxRatePercent,
        e.IsTaxable, e.DisplayLabel, e.Notes, e.IsActive,
    };
}

public sealed class CreatePriceListItemValidator : AbstractValidator<CreatePriceListItemRequest>
{
    public CreatePriceListItemValidator()
    {
        RuleFor(x => x.ServiceId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.BasePrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TaxRatePercent).InclusiveBetween(0, 100);
        RuleFor(x => x.MinimumQuantity).GreaterThanOrEqualTo(1);
        // display_label is first-class: NULL labels made the customer app render UUID
        // fragments. Require a non-empty label so every priced row is human-readable.
        RuleFor(x => x.DisplayLabel)
            .NotEmpty().WithMessage("Display label is required.")
            .MaximumLength(200);
    }
}

public sealed class UpdatePriceListItemValidator : AbstractValidator<UpdatePriceListItemRequest>
{
    public UpdatePriceListItemValidator()
    {
        RuleFor(x => x.BasePrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TaxRatePercent).InclusiveBetween(0, 100);
        RuleFor(x => x.MinimumQuantity).GreaterThanOrEqualTo(1);
        RuleFor(x => x.DisplayLabel)
            .NotEmpty().WithMessage("Display label is required.")
            .MaximumLength(200);
    }
}
