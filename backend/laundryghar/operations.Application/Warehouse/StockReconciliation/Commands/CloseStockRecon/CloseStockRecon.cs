using System.Text.Json;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.StockReconciliation.Commands.CreateStockRecon;
using operations.Application.Warehouse.StockReconciliation.Dtos;

namespace operations.Application.Warehouse.StockReconciliation.Commands.CloseStockRecon;

public sealed record CloseStockReconCommand(Guid ReconId, CloseReconRequest Request, Guid? ActorId)
    : ICommand<StockReconciliationDto?>;

public sealed class CloseStockReconCommandHandler : ICommandHandler<CloseStockReconCommand, StockReconciliationDto?>
{
    private readonly IOperationsDbContext                  _db;
    private readonly ICurrentUser                          _user;
    private readonly ILogger<CloseStockReconCommandHandler> _logger;

    public CloseStockReconCommandHandler(
        IOperationsDbContext                  db,
        ICurrentUser                          user,
        ILogger<CloseStockReconCommandHandler> logger)
    {
        _db     = db;
        _user   = user;
        _logger = logger;
    }

    public async Task<StockReconciliationDto?> HandleAsync(CloseStockReconCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var cmd = command;
        var ct = cancellationToken;
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
        return CreateStockReconCommandHandler.ToDto(recon);
    }
}
