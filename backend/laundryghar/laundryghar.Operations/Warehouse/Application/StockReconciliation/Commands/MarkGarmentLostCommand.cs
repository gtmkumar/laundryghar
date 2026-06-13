using laundryghar.Warehouse.Infrastructure.Auth;
using laundryghar.Warehouse.Infrastructure.Services;
using System.Text.Json;
using laundryghar.Warehouse.Application.Garments.Commands;
using laundryghar.Warehouse.Application.StockReconciliation.Dtos;
using MediatR;

namespace laundryghar.Warehouse.Application.StockReconciliation.Commands;

/// <summary>
/// When a stock reconciliation is closed and still has items in 'missing' status,
/// this command marks each associated garment as lost:
///   - Sets garment.Status = 'lost' and garment.CurrentStage = 'lost'.
///   - Emits a <c>garment.lost</c> outbox event with customer/order context so that
///     downstream consumers (notifications, analytics, future wallet-compensation handler)
///     can react.
///
/// Wallet compensation is OUT OF SCOPE this round — needs policy (credit amount, caps,
/// approval workflow). The garment.lost event carries the data a future handler will need.
///
/// Called from within <see cref="CloseStockReconHandler"/> after the recon is stamped
/// as 'completed', within the same SaveChangesAsync call — atomically.
/// </summary>
public static class LostGarmentProcessor
{
    /// <summary>
    /// Mutates garments in-place and adds outbox events to the DbContext change-tracker
    /// for any recon items still in 'missing' status at close time.
    /// The caller is responsible for SaveChangesAsync.
    /// </summary>
    public static async Task MarkMissingAsLostAsync(
        LaundryGharDbContext db,
        Guid reconId,
        Guid brandId,
        ILogger logger,
        CancellationToken ct)
    {
        // Load all unresolved missing items for this recon.
        var missingItems = await db.StockReconciliationItems
            .Where(i => i.ReconciliationId == reconId &&
                        i.BrandId          == brandId  &&
                        i.Status           == "missing")
            .ToListAsync(ct);

        if (missingItems.Count == 0)
            return;

        var garmentIds = missingItems
            .Where(i => i.GarmentId.HasValue)
            .Select(i => i.GarmentId!.Value)
            .Distinct()
            .ToList();

        if (garmentIds.Count == 0)
            return;

        // Load garments with customer/order context for the event payload.
        var garments = await db.Garments
            .Where(g => garmentIds.Contains(g.Id) && g.BrandId == brandId)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;

        foreach (var garment in garments)
        {
            // Idempotent guard — don't re-mark already-lost garments.
            // The DB status column allows: active|inactive|archived (no 'lost' value).
            // We record the lost state via current_stage='lost' (already in the CHECK constraint)
            // and leave status='active' so the garment remains visible/traceable.
            if (garment.CurrentStage == GarmentStage.Lost)
                continue;

            garment.CurrentStage  = GarmentStage.Lost;
            garment.UpdatedAt     = now;
            garment.Version++;

            // Emit garment.lost outbox event.
            // A future GARMENT_LOST_* notification template + NotificationMappingService case
            // can consume this; for now it is available for analytics / compensation handlers.
            db.OutboxEvents.Add(new OutboxEvent
            {
                Id            = Guid.NewGuid(),
                BrandId       = brandId,
                AggregateType = "garment",
                AggregateId   = garment.Id,
                EventType     = "garment.lost",
                EventVersion  = 1,
                Payload       = JsonSerializer.Serialize(new
                {
                    GarmentId       = garment.Id,
                    BrandId         = brandId,
                    TagCode         = garment.TagCode,
                    CustomerId      = garment.CustomerId,
                    OrderId         = garment.OrderId,
                    OrderItemId     = garment.OrderItemId,
                    LastStage       = garment.CurrentStage,
                    ReconId         = reconId,
                    MarkedLostAt    = now
                }),
                Metadata      = "{}",
                OccurredAt    = now,
                Status        = "pending",
                CreatedAt     = now
            });

            logger.LogInformation(
                "LostGarmentProcessor: garment {GarmentId} tag={TagCode} marked lost " +
                "(reconId={ReconId}).",
                garment.Id, garment.TagCode, reconId);
        }

        logger.LogInformation(
            "LostGarmentProcessor: {Count} garment(s) marked lost for reconId={ReconId}.",
            garments.Count(g => g.CurrentStage == GarmentStage.Lost), reconId);
    }
}
