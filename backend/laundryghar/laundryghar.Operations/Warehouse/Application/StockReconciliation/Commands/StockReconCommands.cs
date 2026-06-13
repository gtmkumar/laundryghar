using laundryghar.Warehouse.Infrastructure.Auth;
using laundryghar.Warehouse.Infrastructure.Services;
using FluentValidation;
using laundryghar.Warehouse.Application.StockReconciliation.Commands;
using laundryghar.Warehouse.Application.StockReconciliation.Dtos;
using MediatR;

namespace laundryghar.Warehouse.Application.StockReconciliation.Commands;

public sealed record CreateStockReconCommand(CreateStockReconciliationRequest Request, Guid? ActorId)
    : IRequest<StockReconciliationDto>;

public sealed class CreateStockReconHandler : IRequestHandler<CreateStockReconCommand, StockReconciliationDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateStockReconHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<StockReconciliationDto> Handle(CreateStockReconCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        var recon = new SharedDataModel.Entities.OrderLifecycle.StockReconciliation
        {
            Id              = Guid.NewGuid(),
            BrandId         = brandId,
            WarehouseId     = req.WarehouseId,
            StoreId         = req.StoreId,
            ReconDate       = req.ReconDate,
            ReconType       = req.ReconType,
            StartedAt       = now,
            StartedBy       = cmd.ActorId ?? Guid.Empty,
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
            CreatedBy       = cmd.ActorId,
            UpdatedBy       = cmd.ActorId
        };

        _db.StockReconciliations.Add(recon);
        await _db.SaveChangesAsync(ct);
        return ToDto(recon);
    }

    internal static StockReconciliationDto ToDto(SharedDataModel.Entities.OrderLifecycle.StockReconciliation r) => new(
        r.Id, r.BrandId, r.WarehouseId, r.StoreId,
        r.ReconDate, r.ReconType, r.StartedAt, r.StartedBy,
        r.CompletedAt, r.ExpectedCount, r.ScannedCount, r.MatchedCount,
        r.MissingCount, r.UnexpectedCount, r.Status, r.CreatedAt);
}

public sealed record AddReconItemCommand(Guid ReconId, AddReconItemRequest Request, Guid? ActorId)
    : IRequest<StockReconciliationItemDto?>;

public sealed class AddReconItemHandler : IRequestHandler<AddReconItemCommand, StockReconciliationItemDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public AddReconItemHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<StockReconciliationItemDto?> Handle(AddReconItemCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var recon = await _db.StockReconciliations
            .FirstOrDefaultAsync(r => r.Id == cmd.ReconId && r.BrandId == brandId, ct);
        if (recon is null) return null;

        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var item = new StockReconciliationItem
        {
            Id               = Guid.NewGuid(),
            ReconciliationId = cmd.ReconId,
            BrandId          = brandId,
            GarmentId        = req.GarmentId,
            TagCode          = req.TagCode,
            ExpectedStage    = req.ExpectedStage,
            ExpectedLocationType = req.ExpectedLocationType,
            FoundStage       = req.FoundStage,
            FoundLocationType = req.FoundLocationType,
            Status           = req.Status,
            FlaggedAt        = now,
            CreatedAt        = now,
            CreatedBy        = cmd.ActorId
        };

        // Update session counters
        recon.ScannedCount++;
        if (req.Status == "matched") recon.MatchedCount++;
        else if (req.Status == "missing") recon.MissingCount++;
        else if (req.Status == "unexpected") recon.UnexpectedCount++;
        recon.UpdatedAt = now;
        recon.UpdatedBy = cmd.ActorId;

        _db.StockReconciliationItems.Add(item);
        await _db.SaveChangesAsync(ct);

        return new StockReconciliationItemDto(
            item.Id, item.ReconciliationId, item.BrandId,
            item.GarmentId, item.TagCode,
            item.ExpectedStage, item.FoundStage,
            item.Status, item.FlaggedAt);
    }
}

public sealed record CloseStockReconCommand(Guid ReconId, CloseReconRequest Request, Guid? ActorId)
    : IRequest<StockReconciliationDto?>;

public sealed class CloseStockReconHandler : IRequestHandler<CloseStockReconCommand, StockReconciliationDto?>
{
    private readonly LaundryGharDbContext          _db;
    private readonly ICurrentUser                  _user;
    private readonly ILogger<CloseStockReconHandler> _logger;

    public CloseStockReconHandler(
        LaundryGharDbContext             db,
        ICurrentUser                     user,
        ILogger<CloseStockReconHandler>  logger)
    {
        _db     = db;
        _user   = user;
        _logger = logger;
    }

    public async Task<StockReconciliationDto?> Handle(CloseStockReconCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var recon = await _db.StockReconciliations
            .FirstOrDefaultAsync(r => r.Id == cmd.ReconId && r.BrandId == brandId, ct);
        if (recon is null) return null;

        if (recon.Status != "in_progress")
            throw new BusinessRuleException("Only an in-progress reconciliation can be closed.");

        var now = DateTimeOffset.UtcNow;
        recon.Status      = "completed";
        recon.CompletedAt = now;
        recon.CompletedBy = cmd.ActorId;
        recon.Notes       = cmd.Request.Notes;
        recon.Summary     = JsonSerializer.Serialize(new
        {
            expected  = recon.ExpectedCount,
            scanned   = recon.ScannedCount,
            matched   = recon.MatchedCount,
            missing   = recon.MissingCount,
            unexpected = recon.UnexpectedCount
        });
        recon.UpdatedAt = now;
        recon.UpdatedBy = cmd.ActorId;

        // ── Lost garment flow ─────────────────────────────────────────────────
        // Any items still in 'missing' status when the recon is closed are confirmed lost.
        // Garments are flagged (status='lost', stage='lost') and a garment.lost outbox
        // event is emitted inside this SaveChangesAsync for atomic consistency.
        await LostGarmentProcessor.MarkMissingAsLostAsync(_db, cmd.ReconId, brandId, _logger, ct);

        await _db.SaveChangesAsync(ct);
        return CreateStockReconHandler.ToDto(recon);
    }
}

public sealed class AddReconItemValidator : AbstractValidator<AddReconItemCommand>
{
    private static readonly string[] AllowedStatuses =
        ["matched","missing","unexpected","damaged","resolved","escalated"];

    public AddReconItemValidator()
    {
        RuleFor(x => x.Request.TagCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Status)
            .Must(s => AllowedStatuses.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", AllowedStatuses)}.");
    }
}

public sealed class CreateStockReconValidator : AbstractValidator<CreateStockReconCommand>
{
    private static readonly string[] AllowedTypes = ["daily","weekly","monthly","adhoc","dispute"];

    public CreateStockReconValidator()
    {
        RuleFor(x => x.Request.ReconType)
            .Must(t => AllowedTypes.Contains(t))
            .WithMessage($"ReconType must be one of: {string.Join(", ", AllowedTypes)}.");
        RuleFor(x => x.Request)
            .Must(r => r.WarehouseId.HasValue || r.StoreId.HasValue)
            .WithMessage("Either WarehouseId or StoreId must be provided.");
    }
}
