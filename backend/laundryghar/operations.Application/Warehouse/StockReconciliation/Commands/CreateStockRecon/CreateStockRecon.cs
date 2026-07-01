using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.StockReconciliation.Dtos;

namespace operations.Application.Warehouse.StockReconciliation.Commands.CreateStockRecon;

public sealed record CreateStockReconCommand(CreateStockReconciliationRequest Request, Guid? ActorId)
    : ICommand<StockReconciliationDto>;

public sealed class CreateStockReconCommandHandler : ICommandHandler<CreateStockReconCommand, StockReconciliationDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreateStockReconCommandHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<StockReconciliationDto> HandleAsync(CreateStockReconCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var req     = command.Request;
        var now     = DateTimeOffset.UtcNow;

        // Include the brand so a brand-scoped admin matches via the ancestor id rather than
        // 403-ing on the store/warehouse leaf.
        if (!_user.IsWithinScope(brandId: brandId, storeId: req.StoreId, warehouseId: req.WarehouseId))
            throw new ForbiddenException("This stock reconciliation is outside your assigned scope.");

        var recon = new laundryghar.SharedDataModel.Entities.OrderLifecycle.StockReconciliation
        {
            Id              = Guid.NewGuid(),
            BrandId         = brandId,
            WarehouseId     = req.WarehouseId,
            StoreId         = req.StoreId,
            ReconDate       = req.ReconDate,
            ReconType       = req.ReconType,
            StartedAt       = now,
            StartedBy       = command.ActorId ?? Guid.Empty,
            Status          = "in_progress",
            Summary         = "{}",
            ExpectedCount   = 0,
            ScannedCount    = 0,
            MatchedCount    = 0,
            MissingCount    = 0,
            UnexpectedCount = 0,
            DamagedCount    = 0,
            ResolvedMissingCount = 0,
            CreatedAt       = now,
            UpdatedAt       = now,
            CreatedBy       = command.ActorId,
            UpdatedBy       = command.ActorId
        };

        _db.StockReconciliations.Add(recon);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(recon);
    }

    internal static StockReconciliationDto ToDto(laundryghar.SharedDataModel.Entities.OrderLifecycle.StockReconciliation r) => new(
        r.Id, r.BrandId, r.WarehouseId, r.StoreId,
        r.ReconDate, r.ReconType, r.StartedAt, r.StartedBy,
        r.CompletedAt, r.ExpectedCount, r.ScannedCount, r.MatchedCount,
        r.MissingCount, r.UnexpectedCount, r.Status, r.CreatedAt);
}

public sealed class CreateStockReconValidator : AbstractValidator<CreateStockReconciliationRequest>
{
    private static readonly string[] AllowedTypes = ["daily","weekly","monthly","adhoc","dispute"];

    public CreateStockReconValidator()
    {
        RuleFor(x => x.ReconType)
            .Must(t => AllowedTypes.Contains(t))
            .WithMessage($"ReconType must be one of: {string.Join(", ", AllowedTypes)}.");
        RuleFor(x => x)
            .Must(r => r.WarehouseId.HasValue || r.StoreId.HasValue)
            .WithMessage("Either WarehouseId or StoreId must be provided.");
    }
}
