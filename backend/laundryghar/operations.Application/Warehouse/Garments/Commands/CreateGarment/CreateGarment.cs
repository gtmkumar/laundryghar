using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Garments.Dtos;

namespace operations.Application.Warehouse.Garments.Commands.CreateGarment;

// ── Create FulfillmentUnit from OrderItem ─────────────────────────────────────────────

public sealed record CreateGarmentCommand(CreateGarmentRequest Request, Guid? ActorId)
    : ICommand<GarmentDto>;

public class CreateGarmentCommandHandler : ICommandHandler<CreateGarmentCommand, GarmentDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreateGarmentCommandHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<GarmentDto> HandleAsync(CreateGarmentCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var req     = command.Request;
        var now     = DateTimeOffset.UtcNow;

        // Read order_item to fill mandatory FK fields
        var oi = await _db.OrderItems
            .FirstOrDefaultAsync(x => x.Id == req.OrderItemId && x.BrandId == brandId, cancellationToken)
            ?? throw new KeyNotFoundException($"OrderItem {req.OrderItemId} not found.");

        // Fetch garment tag and mark it assigned
        var tag = await _db.FulfillmentUnitTags
            .FirstOrDefaultAsync(t => t.TagCode == req.TagCode && t.BrandId == brandId
                                   && t.Status == "available", cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Tag '{req.TagCode}' not found or not available for this brand.");

        // Resolve franchise/store from order
        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == oi.OrderId && o.BrandId == brandId, cancellationToken)
            ?? throw new KeyNotFoundException("Order not found.");

        if (!_user.IsWithinScope(franchiseId: order.FranchiseId, storeId: oi.StoreId, warehouseId: req.WarehouseId))
            throw new ForbiddenException("This garment is outside your assigned scope.");

        var garment = new FulfillmentUnit
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
            CurrentStage   = "received",
            Attributes     = new LaundryUnitAttributes
            {
                WeightGrams    = req.WeightGrams,
                HasOrnaments   = req.HasOrnaments,
                HasLining      = req.HasLining,
                IsDesignerWear = req.IsDesignerWear,
                RewashCount    = 0
            },
            Metadata       = "{}",
            Status         = "active",
            CreatedAt      = now,
            UpdatedAt      = now,
            Version        = 1,
            CreatedBy      = command.ActorId,
            UpdatedBy      = command.ActorId
        };

        tag.AssignedToFulfillmentUnitId = garment.Id;
        tag.AssignedAt          = now;
        tag.AssignedBy          = command.ActorId;
        tag.Status              = "assigned";
        tag.UpdatedAt           = now;

        _db.FulfillmentUnits.Add(garment);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(garment);
    }

    internal static GarmentDto ToDto(FulfillmentUnit g) => new(
        g.Id, g.BrandId, g.StoreId, g.WarehouseId,
        g.OrderId, g.OrderItemId, g.CustomerId,
        g.TagCode, g.SecondaryTagCode,
        g.ItemId, g.ItemVariantId, g.FabricTypeId,
        g.Color, g.Size, g.Attributes.WeightGrams,
        g.Attributes.HasOrnaments, g.Attributes.HasLining, g.Attributes.IsDesignerWear,
        g.CurrentStage, g.CurrentBatchId,
        g.LastScannedAt, g.Attributes.RewashCount,
        g.Status, g.CreatedAt, g.UpdatedAt);
}

public sealed class CreateGarmentRequestValidator : AbstractValidator<CreateGarmentRequest>
{
    public CreateGarmentRequestValidator()
    {
        RuleFor(x => x.OrderItemId).NotEmpty();
        RuleFor(x => x.TagCode).NotEmpty().MaximumLength(50);
    }
}
