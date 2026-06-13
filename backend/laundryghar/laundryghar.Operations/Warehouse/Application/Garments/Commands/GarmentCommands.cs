using FluentValidation;
using laundryghar.Warehouse.Application.Garments.Dtos;
using MediatR;

namespace laundryghar.Warehouse.Application.Garments.Commands;

// ── Create Garment from OrderItem ─────────────────────────────────────────────

public sealed record CreateGarmentCommand(CreateGarmentRequest Request, Guid? ActorId)
    : IRequest<GarmentDto>;

public sealed class CreateGarmentHandler : IRequestHandler<CreateGarmentCommand, GarmentDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateGarmentHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<GarmentDto> Handle(CreateGarmentCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        // Read order_item to fill mandatory FK fields
        var oi = await _db.OrderItems
            .FirstOrDefaultAsync(x => x.Id == req.OrderItemId && x.BrandId == brandId, ct)
            ?? throw new KeyNotFoundException($"OrderItem {req.OrderItemId} not found.");

        // Fetch garment tag and mark it assigned
        var tag = await _db.GarmentTags
            .FirstOrDefaultAsync(t => t.TagCode == req.TagCode && t.BrandId == brandId
                                   && t.Status == "available", ct)
            ?? throw new KeyNotFoundException(
                $"Tag '{req.TagCode}' not found or not available for this brand.");

        // Resolve franchise/store from order
        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == oi.OrderId && o.BrandId == brandId, ct)
            ?? throw new KeyNotFoundException("Order not found.");

        var garment = new Garment
        {
            Id             = Guid.NewGuid(),
            BrandId        = brandId,
            FranchiseId    = order.FranchiseId,
            StoreId        = oi.StoreId,
            WarehouseId    = req.WarehouseId,
            OrderId        = oi.OrderId,
            OrderCreatedAt = oi.OrderCreatedAt,
            OrderItemId    = req.OrderItemId,
            CustomerId     = order.CustomerId,
            TagCode        = req.TagCode,
            ItemId         = oi.ItemId,
            ItemVariantId  = oi.ItemVariantId,
            Color          = req.Color,
            Size           = req.Size,
            WeightGrams    = req.WeightGrams,
            HasOrnaments   = req.HasOrnaments,
            HasLining      = req.HasLining,
            IsDesignerWear = req.IsDesignerWear,
            CurrentStage   = "received",
            RewashCount    = 0,
            Metadata       = "{}",
            Status         = "active",
            CreatedAt      = now,
            UpdatedAt      = now,
            Version        = 1,
            CreatedBy      = cmd.ActorId,
            UpdatedBy      = cmd.ActorId
        };

        tag.AssignedToGarmentId = garment.Id;
        tag.AssignedAt          = now;
        tag.AssignedBy          = cmd.ActorId;
        tag.Status              = "assigned";
        tag.UpdatedAt           = now;

        _db.Garments.Add(garment);
        await _db.SaveChangesAsync(ct);
        return ToDto(garment);
    }

    internal static GarmentDto ToDto(Garment g) => new(
        g.Id, g.BrandId, g.StoreId, g.WarehouseId,
        g.OrderId, g.OrderItemId, g.CustomerId,
        g.TagCode, g.SecondaryTagCode,
        g.ItemId, g.ItemVariantId, g.FabricTypeId,
        g.Color, g.Size, g.WeightGrams,
        g.HasOrnaments, g.HasLining, g.IsDesignerWear,
        g.CurrentStage, g.CurrentBatchId,
        g.LastScannedAt, g.RewashCount,
        g.Status, g.CreatedAt, g.UpdatedAt);
}

public sealed class CreateGarmentValidator : AbstractValidator<CreateGarmentCommand>
{
    public CreateGarmentValidator()
    {
        RuleFor(x => x.Request.OrderItemId).NotEmpty();
        RuleFor(x => x.Request.TagCode).NotEmpty().MaximumLength(50);
    }
}

// ── Generate Tags ─────────────────────────────────────────────────────────────

public sealed record GenerateTagsCommand(GenerateTagsRequest Request, Guid? ActorId)
    : IRequest<IReadOnlyList<GarmentTagDto>>;

public sealed class GenerateTagsHandler : IRequestHandler<GenerateTagsCommand, IReadOnlyList<GarmentTagDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GenerateTagsHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<IReadOnlyList<GarmentTagDto>> Handle(GenerateTagsCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        // Base for sequential codes
        var existing = await _db.GarmentTags
            .Where(t => t.BrandId == brandId)
            .CountAsync(ct);

        var tags = Enumerable.Range(1, req.Count).Select(i => new GarmentTag
        {
            Id          = Guid.NewGuid(),
            BrandId     = brandId,
            TagCode     = $"LG-{brandId.ToString()[..4].ToUpper()}-{(existing + i):D8}",
            TagFormat   = req.TagFormat,
            BatchNumber = req.BatchNumber,
            PrintedAt   = now,
            PrintedBy   = cmd.ActorId,
            Status      = "available",
            CreatedAt   = now,
            UpdatedAt   = now,
            CreatedBy   = cmd.ActorId
        }).ToList();

        _db.GarmentTags.AddRange(tags);
        await _db.SaveChangesAsync(ct);

        return tags.Select(t => new GarmentTagDto(
            t.Id, t.BrandId, t.TagCode, t.TagFormat,
            t.BatchNumber, t.AssignedToGarmentId,
            t.AssignedAt, t.IsDamaged, t.Status, t.CreatedAt)).ToList();
    }
}

public sealed class GenerateTagsValidator : AbstractValidator<GenerateTagsCommand>
{
    private static readonly string[] AllowedFormats = ["qr","barcode_128","barcode_39","rfid"];

    public GenerateTagsValidator()
    {
        RuleFor(x => x.Request.Count).InclusiveBetween(1, 200);
        RuleFor(x => x.Request.TagFormat)
            .Must(f => AllowedFormats.Contains(f))
            .WithMessage($"TagFormat must be one of: {string.Join(", ", AllowedFormats)}.");
    }
}
