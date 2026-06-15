using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Catalog.Commands.ItemVariant;

public sealed record CreateItemVariantCommand(CreateItemVariantRequest Request, Guid? ActorId) : ICommand<ItemVariantDto>;

public sealed class CreateItemVariantHandler : ICommandHandler<CreateItemVariantCommand, ItemVariantDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreateItemVariantHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<ItemVariantDto> HandleAsync(CreateItemVariantCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var e = new laundryghar.SharedDataModel.Entities.CustomerCatalog.ItemVariant
        {
            Id           = Guid.NewGuid(),
            BrandId      = brandId,
            ItemId       = req.ItemId,
            FabricTypeId = req.FabricTypeId,
            Code         = req.Code,
            VariantName  = req.VariantName,
            Side         = req.Side,
            Size         = req.Size,
            Color        = req.Color,
            Sku          = req.Sku,
            Barcode      = req.Barcode,
            DisplayOrder = req.DisplayOrder,
            Status       = "active",
            CreatedAt    = now,
            UpdatedAt    = now,
            CreatedBy    = cmd.ActorId,
            UpdatedBy    = cmd.ActorId
        };

        _db.ItemVariants.Add(e);
        await _db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    internal static ItemVariantDto ToDto(laundryghar.SharedDataModel.Entities.CustomerCatalog.ItemVariant e) => new(
        e.Id, e.BrandId, e.ItemId, e.FabricTypeId, e.Code, e.VariantName,
        e.Side, e.Size, e.Color, e.Sku, e.Barcode, e.DisplayOrder, e.Status,
        e.CreatedAt, e.UpdatedAt);
}

public sealed record UpdateItemVariantCommand(Guid Id, UpdateItemVariantRequest Request, Guid? ActorId) : ICommand<ItemVariantDto?>;

public sealed class UpdateItemVariantHandler : ICommandHandler<UpdateItemVariantCommand, ItemVariantDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateItemVariantHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ItemVariantDto?> HandleAsync(UpdateItemVariantCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.ItemVariants
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return null;

        var req = cmd.Request;
        e.FabricTypeId = req.FabricTypeId;
        e.VariantName  = req.VariantName;
        e.Side         = req.Side;
        e.Size         = req.Size;
        e.Color        = req.Color;
        e.Sku          = req.Sku;
        e.Barcode      = req.Barcode;
        e.DisplayOrder = req.DisplayOrder;
        e.Status       = req.Status;
        e.UpdatedAt    = DateTimeOffset.UtcNow;
        e.UpdatedBy    = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return CreateItemVariantHandler.ToDto(e);
    }
}

public sealed record DeleteItemVariantCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public sealed class DeleteItemVariantHandler : ICommandHandler<DeleteItemVariantCommand, bool>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteItemVariantHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> HandleAsync(DeleteItemVariantCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.ItemVariants
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return false;

        // Soft-delete must also move status off 'active' so status-keyed reports don't
        // miscount it. item_variants CHECK is ('active','disabled') — 'disabled' is terminal.
        e.Status    = "disabled";
        e.DeletedAt = DateTimeOffset.UtcNow;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class CreateItemVariantValidator : AbstractValidator<CreateItemVariantRequest>
{
    public CreateItemVariantValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.VariantName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ItemId).NotEmpty();
    }
}
