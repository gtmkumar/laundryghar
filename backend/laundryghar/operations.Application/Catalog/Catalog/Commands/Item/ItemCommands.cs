using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Common;
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
            Code                  = req.Code,
            Name                  = req.Name,
            NameLocalized         = req.NameLocalized,
            Description           = req.Description,
            IconUrl               = req.IconUrl,
            ImageUrl              = req.ImageUrl,
            TypicalWeightGrams    = req.TypicalWeightGrams,
            RequiresPerSidePrice  = req.RequiresPerSidePrice,
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
        await _db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    internal static ItemDto ToDto(laundryghar.SharedDataModel.Entities.CustomerCatalog.Item e) => new(
        e.Id, e.BrandId, e.ItemGroupId, e.Code, e.Name, e.NameLocalized,
        e.Description, e.IconUrl, e.ImageUrl, e.TypicalWeightGrams,
        e.RequiresPerSidePrice, e.Aliases, e.DisplayOrder, e.Status, e.CreatedAt, e.UpdatedAt);
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
        e.Aliases             = req.Aliases ?? [];
        e.DisplayOrder        = req.DisplayOrder;
        e.Status              = req.Status;
        e.UpdatedAt           = DateTimeOffset.UtcNow;
        e.UpdatedBy           = cmd.ActorId;
        e.Version++;

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

        // Soft-delete must also move status off 'active' so status-keyed reports don't
        // miscount it. items CHECK is ('active','disabled','seasonal') — 'disabled' is
        // the archived/retired equivalent.
        e.Status    = "disabled";
        e.DeletedAt = DateTimeOffset.UtcNow;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;
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
    }
}
