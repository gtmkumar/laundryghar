using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Logistics.Cod;

// ── DTOs ──────────────────────────────────────────────────────────────────────

/// <summary>Settle request body — every field optional. When <see cref="StoreId"/> is omitted
/// the rider's primary store is used (may itself be null → no cash-book posting).</summary>
public sealed record SettleRiderPayload(Guid? StoreId, string? Reference, string? Notes);

/// <summary>A recorded rider COD handover — the settlement row plus its resolved store name.
/// Shared by the settle command and the settlement-history query.</summary>
public sealed record RiderSettlementDto(
    Guid Id,
    Guid RiderId,
    Guid? StoreId,
    string? StoreName,
    decimal TotalAmount,
    int CollectionCount,
    string? Reference,
    string Status,
    DateTimeOffset SettledAt,
    Guid? SettledBy,
    string? Notes);

// ── Command ───────────────────────────────────────────────────────────────────

/// <summary>
/// Records that the rider handed over ALL outstanding COD cash: creates one settlement row and
/// stamps its id onto every uncleared collection (same predicate as <see cref="GetCodOutstandingQuery"/>),
/// then posts a matching cash_in entry into the store's open cash book. Missing rider → null (404);
/// out-of-scope rider → 403; nothing outstanding → 422 business rule. RLS scopes by brand; the
/// explicit brand filter is defense-in-depth.
/// </summary>
public sealed record SettleRiderCodCommand(Guid BrandId, Guid RiderId, SettleRiderPayload? Payload, Guid? ActorId)
    : ICommand<RiderSettlementDto?>;

public sealed class SettleRiderCodHandler : ICommandHandler<SettleRiderCodCommand, RiderSettlementDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public SettleRiderCodHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<RiderSettlementDto?> HandleAsync(SettleRiderCodCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var now = DateTimeOffset.UtcNow;

        // Load the rider (brand-scoped defense-in-depth on top of RLS). Missing → 404.
        var rider = await _db.Riders.FirstOrDefaultAsync(
            r => r.Id == command.RiderId && r.BrandId == command.BrandId, ct);
        if (rider is null) return null;

        // Store the settlement is booked against: explicit override, else the rider's primary
        // store (may be null → settlement still records, but there is no cash-book to post into).
        var storeId = command.Payload?.StoreId ?? rider.PrimaryStoreId;

        // §6 sub-brand boundary: brand-level RLS alone would let a franchise/store-scoped operator
        // settle a rider outside their subtree within the same brand. Enforce ancestor-or-self.
        if (!_user.IsWithinScope(brandId: rider.BrandId, franchiseId: rider.FranchiseId, storeId: storeId))
            throw new ForbiddenException("This rider is outside your assigned scope.");

        // Load every uncleared collection for this rider (same filter as GetCodOutstanding).
        // Tracked (not AsNoTracking) — we stamp settlement_id on each below.
        var collections = await _db.DeliveryAssignments
            .Where(d => d.BrandId == command.BrandId
                     && d.RiderId == command.RiderId
                     && d.CodAmount > 0m
                     && d.CodCollectedAt != null
                     && d.SettlementId == null)
            .ToListAsync(ct);

        if (collections.Count == 0)
            throw new BusinessRuleException("This rider has no outstanding COD cash to settle.");

        var totalAmount = collections.Sum(d => d.CodAmount ?? 0m);
        var collectionCount = collections.Count;

        var settlement = new RiderSettlement
        {
            Id = Guid.NewGuid(),
            BrandId = rider.BrandId,
            FranchiseId = rider.FranchiseId,
            RiderId = rider.Id,
            StoreId = storeId,
            TotalAmount = totalAmount,
            CollectionCount = collectionCount,
            Reference = command.Payload?.Reference,
            Status = "settled",
            SettledAt = now,
            SettledBy = command.ActorId,
            Notes = command.Payload?.Notes,
            Metadata = "{}",
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = command.ActorId,
            UpdatedBy = command.ActorId,
        };
        _db.RiderSettlements.Add(settlement);

        // Clear every covered collection by stamping the new settlement id.
        foreach (var d in collections)
        {
            d.SettlementId = settlement.Id;
            d.UpdatedAt = now;
            d.UpdatedBy = command.ActorId;
        }

        // Cash IN mirror of the payout handler's cash_out posting (COD cash physically handed over).
        await PostCashInAsync(settlement, rider, storeId, totalAmount, now, command.ActorId, ct);

        // Single unit of work: settlement insert + assignment stamps + cash-book posting commit
        // atomically. The entity graph is built ABOVE the closure so a strategy retry re-runs only
        // SaveChanges over the same tracked changes (no duplicate inserts). Concurrency: this handler
        // only selects settlement_id IS NULL rows, so a serialized double-settle finds nothing
        // outstanding on the second pass and raises the rule above; the transaction bounds it — full
        // isolation (a conditional UPDATE / row lock) is intentionally out of scope here.
        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            await _db.SaveChangesAsync(innerCt);
        }, ct);

        // Store display name (cross lookup, display-only — resolved after the commit).
        string? storeName = storeId is Guid sid
            ? await _db.Stores.AsNoTracking().Where(s => s.Id == sid).Select(s => s.Name).FirstOrDefaultAsync(ct)
            : null;

        return new RiderSettlementDto(
            settlement.Id, settlement.RiderId, settlement.StoreId, storeName,
            settlement.TotalAmount, settlement.CollectionCount, settlement.Reference,
            settlement.Status, settlement.SettledAt, settlement.SettledBy, settlement.Notes);
    }

    // Mirrors MarkPayoutPaidHandler.PostCashOutAsync, but cash IN / order_payment / rider_settlement.
    // Category is order_payment — the closest allowed value (the cash_book_entries.category CHECK has
    // no "cod"/"collection"); this matches RecordOfflinePaymentHandler, which posts collected order
    // cash the same way. Skipped when there is no store or the day's book is not open.
    private async Task PostCashInAsync(
        RiderSettlement settlement, Rider rider, Guid? storeId,
        decimal totalAmount, DateTimeOffset now, Guid? actorId, CancellationToken ct)
    {
        if (storeId is not Guid store) return;   // no store → nothing to post against
        if (totalAmount <= 0m) return;
        var bookDate = DateOnly.FromDateTime(now.UtcDateTime);

        var book = await _db.CashBooks.FirstOrDefaultAsync(
            b => b.BrandId == settlement.BrandId && b.StoreId == store
              && b.BookDate == bookDate && b.ShiftLabel == "full_day", ct);

        if (book is null)
        {
            book = new CashBook
            {
                Id = Guid.NewGuid(), BrandId = settlement.BrandId, FranchiseId = rider.FranchiseId,
                StoreId = store, BookDate = bookDate, ShiftLabel = "full_day",
                OpeningUserId = actorId ?? Guid.Empty, OpeningBalance = 0, CashInflow = 0, CashOutflow = 0,
                UpiInflow = 0, CardInflow = 0, OtherInflow = 0, DepositAmount = 0,
                TotalOrders = 0, NewOrders = 0, DeliveredOrders = 0, CancelledOrders = 0,
                Status = "open", Metadata = "{}", OpenedAt = now, CreatedAt = now, UpdatedAt = now,
                CreatedBy = actorId, UpdatedBy = actorId,
            };
            _db.CashBooks.Add(book);
        }
        else if (book.Status != "open")
        {
            return;   // book closed → settlement stands alone (no posting)
        }

        _db.CashBookEntries.Add(new CashBookEntry
        {
            Id = Guid.NewGuid(), CashBookId = book.Id, BrandId = settlement.BrandId, StoreId = store,
            EntryType = "cash_in", Category = "order_payment", Direction = 1, Amount = totalAmount,
            PaymentMode = "cash", ReferenceType = "rider_settlement", ReferenceId = settlement.Id,
            Description = $"Rider COD settlement{(string.IsNullOrWhiteSpace(settlement.Reference) ? "" : $" ({settlement.Reference})")}",
            PerformedBy = actorId ?? Guid.Empty, OccurredAt = now, Metadata = "{}",
            CreatedAt = now, CreatedBy = actorId,
        });

        book.CashInflow += totalAmount;
        book.UpdatedAt = now;
        book.UpdatedBy = actorId;
    }
}
