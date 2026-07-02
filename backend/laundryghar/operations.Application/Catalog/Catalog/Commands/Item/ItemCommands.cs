using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Common;
using operations.Application.Catalog.Pricing.Common;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Catalog.Commands.Item;

public sealed record CreateItemCommand(CreateItemRequest Request, Guid? ActorId) : ICommand<ItemDto>;

public sealed class CreateItemHandler : ICommandHandler<CreateItemCommand, ItemDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreateItemHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<ItemDto> HandleAsync(CreateItemCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var e = new laundryghar.SharedDataModel.Entities.CustomerCatalog.Item
        {
            Id                    = Guid.NewGuid(),
            BrandId               = brandId,
            ItemGroupId           = req.ItemGroupId,
            // Vertical-neutral discriminator; defaults to laundry until brand-vertical resolution lands.
            CatalogKind           = req.CatalogKind ?? laundryghar.SharedDataModel.Enums.CatalogKind.LaundryGarment,
            PricingMode           = req.PricingMode ?? laundryghar.SharedDataModel.Enums.PricingMode.Standard,
            Code                  = req.Code,
            Name                  = req.Name,
            NameLocalized         = req.NameLocalized,
            Description           = req.Description,
            IconUrl               = req.IconUrl,
            ImageUrl              = req.ImageUrl,
            TypicalWeightGrams    = req.TypicalWeightGrams,
            RequiresPerSidePrice  = req.RequiresPerSidePrice,
            TatHours              = req.TatHours,
            ExpressEligible       = req.ExpressEligible,
            ExpressSurcharge      = req.ExpressSurcharge,
            // SearchTokens is DB-managed (tsvector) — do NOT write it
            Aliases               = req.Aliases ?? [],
            DisplayOrder          = req.DisplayOrder,
            Status                = "active",
            CreatedAt             = now,
            UpdatedAt             = now,
            CreatedBy             = cmd.ActorId,
            UpdatedBy             = cmd.ActorId,
            Version               = 1
        };

        _db.Items.Add(e);

        // Audit: a create carries no before-state; the after-state is the new item's editable fields.
        // (GH #24 item audit — surfaces in the pricing Change history feed; create is not revertible.)
        PricingChangeLogger.Add(_db, brandId, "item", e.Id,
            $"Item created: {e.Name} ({e.Code})",
            new ItemAuditEnvelope(ItemAudit.OpCreate, null),
            new ItemAuditEnvelope(ItemAudit.OpCreate, ItemAudit.Capture(e)),
            cmd.ActorId, _user.Email);

        await _db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    internal static ItemDto ToDto(laundryghar.SharedDataModel.Entities.CustomerCatalog.Item e) => new(
        e.Id, e.BrandId, e.ItemGroupId, e.Code, e.Name, e.NameLocalized,
        e.Description, e.IconUrl, e.ImageUrl, e.TypicalWeightGrams,
        e.RequiresPerSidePrice, e.Aliases, e.DisplayOrder, e.Status, e.CreatedAt, e.UpdatedAt,
        e.TatHours, e.ExpressEligible, e.ExpressSurcharge, e.CatalogKind, e.PricingMode);
}

public sealed record UpdateItemCommand(Guid Id, UpdateItemRequest Request, Guid? ActorId) : ICommand<ItemDto?>;

public sealed class UpdateItemHandler : ICommandHandler<UpdateItemCommand, ItemDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateItemHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ItemDto?> HandleAsync(UpdateItemCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Items
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return null;

        var req = cmd.Request;
        if (req.PricingMode is not null && !laundryghar.SharedDataModel.Enums.PricingMode.IsValid(req.PricingMode))
            throw new laundryghar.Utilities.Exceptions.BusinessRuleException(
                $"PricingMode must be one of: {string.Join(", ", laundryghar.SharedDataModel.Enums.PricingMode.All)}.");

        // Snapshot the before-state up front so the audit + revert can restore the exact prior values.
        var before = ItemAudit.Capture(e);

        // Optional SKU/code change (GH #24). Null → leave the code unchanged (partial PUT). A real
        // change must stay unique among the brand's non-deleted items, else a structured 422 the
        // client can branch on. Compared case-sensitively — codes are stored verbatim.
        if (req.Code is not null && !string.Equals(req.Code, e.Code, StringComparison.Ordinal))
        {
            var newCode = req.Code.Trim();
            if (newCode.Length == 0)
                throw new BusinessRuleException("Item code cannot be blank.");

            var taken = await _db.Items.AnyAsync(
                x => x.BrandId == brandId && x.Id != e.Id && x.DeletedAt == null && x.Code == newCode, ct);
            if (taken)
                throw new StructuredBusinessRuleException(
                    "item_code_taken",
                    $"Another item already uses the code “{newCode}”. Choose a unique code.",
                    new Dictionary<string, string> { ["code"] = newCode });

            e.Code = newCode;
        }

        e.ItemGroupId         = req.ItemGroupId;
        e.Name                = req.Name;
        e.NameLocalized       = req.NameLocalized;
        e.Description         = req.Description;
        // Icon/image are managed via the dedicated image endpoints; a null here
        // means "leave unchanged" so a generic PUT can't wipe an uploaded image.
        e.IconUrl             = req.IconUrl ?? e.IconUrl;
        e.ImageUrl            = req.ImageUrl ?? e.ImageUrl;
        e.TypicalWeightGrams  = req.TypicalWeightGrams;
        e.RequiresPerSidePrice = req.RequiresPerSidePrice;
        e.TatHours            = req.TatHours;
        e.ExpressEligible     = req.ExpressEligible;
        e.ExpressSurcharge    = req.ExpressSurcharge;
        // Null → leave the item's current pricing mode unchanged (partial PUTs must not reset it).
        e.PricingMode         = req.PricingMode ?? e.PricingMode;
        e.Aliases             = req.Aliases ?? [];
        e.DisplayOrder        = req.DisplayOrder;
        e.Status              = req.Status;
        e.UpdatedAt           = DateTimeOffset.UtcNow;
        e.UpdatedBy           = cmd.ActorId;
        e.Version++;

        // Audit the old→new editable fields. This UPDATE envelope is the one Revert can restore.
        PricingChangeLogger.Add(_db, brandId, "item", e.Id,
            $"Item updated: {e.Name} ({e.Code})",
            new ItemAuditEnvelope(ItemAudit.OpUpdate, before),
            new ItemAuditEnvelope(ItemAudit.OpUpdate, ItemAudit.Capture(e)),
            cmd.ActorId, _user.Email);

        await _db.SaveChangesAsync(ct);
        return CreateItemHandler.ToDto(e);
    }
}

public sealed record DeleteItemCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public sealed class DeleteItemHandler : ICommandHandler<DeleteItemCommand, bool>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteItemHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> HandleAsync(DeleteItemCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Items
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return false;

        // Snapshot before mutating so the audit records the item as it stood at delete time.
        var before = ItemAudit.Capture(e);

        // Soft-delete must also move status off 'active' so status-keyed reports don't
        // miscount it. items CHECK is ('active','disabled','seasonal') — 'disabled' is
        // the archived/retired equivalent.
        e.Status    = "disabled";
        e.DeletedAt = DateTimeOffset.UtcNow;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;

        // Audit: a delete carries the prior state as before, no after. Not revertible.
        PricingChangeLogger.Add(_db, brandId, "item", e.Id,
            $"Item deleted: {before.Name} ({before.Code})",
            new ItemAuditEnvelope(ItemAudit.OpDelete, before),
            new ItemAuditEnvelope(ItemAudit.OpDelete, null),
            cmd.ActorId, _user.Email);

        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class CreateItemValidator : AbstractValidator<CreateItemRequest>
{
    public CreateItemValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameLocalized).NotEmpty().MaximumLength(200).MustBeJsonObject();
        RuleFor(x => x.CatalogKind!)
            .Must(laundryghar.SharedDataModel.Enums.CatalogKind.IsValid)
            .When(x => x.CatalogKind is not null)
            .WithMessage($"CatalogKind must be one of: {string.Join(", ", laundryghar.SharedDataModel.Enums.CatalogKind.All)}.");
        RuleFor(x => x.PricingMode!)
            .Must(laundryghar.SharedDataModel.Enums.PricingMode.IsValid)
            .When(x => x.PricingMode is not null)
            .WithMessage($"PricingMode must be one of: {string.Join(", ", laundryghar.SharedDataModel.Enums.PricingMode.All)}.");
    }
}
