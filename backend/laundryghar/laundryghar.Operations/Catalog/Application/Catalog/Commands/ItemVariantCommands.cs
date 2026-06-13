using FluentValidation;
using laundryghar.Catalog.Application.Catalog.Dtos;
using MediatR;

namespace laundryghar.Catalog.Application.Catalog.Commands;

public sealed record CreateItemVariantCommand(CreateItemVariantRequest Request, Guid? ActorId) : IRequest<ItemVariantDto>;

public sealed class CreateItemVariantHandler : IRequestHandler<CreateItemVariantCommand, ItemVariantDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateItemVariantHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<ItemVariantDto> Handle(CreateItemVariantCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var e = new ItemVariant
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

    internal static ItemVariantDto ToDto(ItemVariant e) => new(
        e.Id, e.BrandId, e.ItemId, e.FabricTypeId, e.Code, e.VariantName,
        e.Side, e.Size, e.Color, e.Sku, e.Barcode, e.DisplayOrder, e.Status,
        e.CreatedAt, e.UpdatedAt);
}

public sealed record UpdateItemVariantCommand(Guid Id, UpdateItemVariantRequest Request, Guid? ActorId) : IRequest<ItemVariantDto?>;

public sealed class UpdateItemVariantHandler : IRequestHandler<UpdateItemVariantCommand, ItemVariantDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateItemVariantHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ItemVariantDto?> Handle(UpdateItemVariantCommand cmd, CancellationToken ct)
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

public sealed record DeleteItemVariantCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

public sealed class DeleteItemVariantHandler : IRequestHandler<DeleteItemVariantCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteItemVariantHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> Handle(DeleteItemVariantCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.ItemVariants
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return false;

        e.DeletedAt = DateTimeOffset.UtcNow;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class CreateItemVariantValidator : AbstractValidator<CreateItemVariantCommand>
{
    public CreateItemVariantValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.VariantName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.ItemId).NotEmpty();
    }
}
