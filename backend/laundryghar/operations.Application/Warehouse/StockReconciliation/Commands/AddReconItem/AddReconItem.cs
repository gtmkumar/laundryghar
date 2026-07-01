using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.StockReconciliation.Dtos;

namespace operations.Application.Warehouse.StockReconciliation.Commands.AddReconItem;

public sealed record AddReconItemCommand(Guid ReconId, AddReconItemRequest Request, Guid? ActorId)
    : ICommand<StockReconciliationItemDto?>;

public sealed class AddReconItemCommandHandler : ICommandHandler<AddReconItemCommand, StockReconciliationItemDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public AddReconItemCommandHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<StockReconciliationItemDto?> HandleAsync(AddReconItemCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var recon = await _db.StockReconciliations
            .FirstOrDefaultAsync(r => r.Id == command.ReconId && r.BrandId == brandId, cancellationToken);
        if (recon is null) return null;

        if (!_user.IsWithinScope(storeId: recon.StoreId, warehouseId: recon.WarehouseId))
            throw new ForbiddenException("This reconciliation session is outside your assigned scope.");

        var req = command.Request;
        var now = DateTimeOffset.UtcNow;

        var item = new StockReconciliationItem
        {
            Id               = Guid.NewGuid(),
            ReconciliationId = command.ReconId,
            BrandId          = brandId,
            FulfillmentUnitId        = req.FulfillmentUnitId,
            TagCode          = req.TagCode,
            ExpectedStage    = req.ExpectedStage,
            ExpectedLocationType = req.ExpectedLocationType,
            FoundStage       = req.FoundStage,
            FoundLocationType = req.FoundLocationType,
            Status           = req.Status,
            FlaggedAt        = now,
            CreatedAt        = now,
            CreatedBy        = command.ActorId
        };

        // Update session counters
        recon.ScannedCount++;
        if (req.Status == "matched") recon.MatchedCount++;
        else if (req.Status == "missing") recon.MissingCount++;
        else if (req.Status == "unexpected") recon.UnexpectedCount++;
        recon.UpdatedAt = now;
        recon.UpdatedBy = command.ActorId;

        _db.StockReconciliationItems.Add(item);
        await _db.SaveChangesAsync(cancellationToken);

        return new StockReconciliationItemDto(
            item.Id, item.ReconciliationId, item.BrandId,
            item.FulfillmentUnitId, item.TagCode,
            item.ExpectedStage, item.FoundStage,
            item.Status, item.FlaggedAt);
    }
}

public sealed class AddReconItemValidator : AbstractValidator<AddReconItemRequest>
{
    private static readonly string[] AllowedStatuses =
        ["matched","missing","unexpected","damaged","resolved","escalated"];

    public AddReconItemValidator()
    {
        RuleFor(x => x.TagCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Status)
            .Must(s => AllowedStatuses.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", AllowedStatuses)}.");
    }
}
