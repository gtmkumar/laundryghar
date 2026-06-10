using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using MediatR;

namespace laundryghar.Logistics.Application.RiderCod;

/// <summary>
/// Record a settlement that clears ALL of a rider's outstanding COD cash: creates a
/// rider_settlements row and stamps its id onto every covered delivery leg. Admin-
/// recorded, one step (status = settled). Returns null → 404 (rider not in scope /
/// nothing outstanding).
///
/// When the settlement names a store, the handed-over cash is also mirrored into the
/// store's finance cash book as a cash_in / order_payment entry (same transaction),
/// finding-or-opening that day's full_day book. Best-effort: skipped (settlement still
/// succeeds) when there's no store or the day's book is already closed.
/// </summary>
public sealed record SettleRiderCodCommand(Guid RiderId, SettleRiderCodRequest Request, Guid? ActorId)
    : IRequest<RiderSettlementDto?>;

public sealed class SettleRiderCodHandler : IRequestHandler<SettleRiderCodCommand, RiderSettlementDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public SettleRiderCodHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    // Domain vocabulary backed by DB CHECK constraints. Named constants (rather than a
    // C# enum) because these persist as plain strings — keeps the EF mapping trivial
    // while removing magic literals.
    private const string SettlementSettled = "settled";   // rider_settlements.status
    private static class Cb
    {
        public const string ShiftFullDay         = "full_day";       // cash_books.shift_label
        public const string StatusOpen           = "open";           // cash_books.status
        public const string EntryCashIn          = "cash_in";        // cash_book_entries.entry_type
        public const string CategoryOrderPayment = "order_payment";  // cash_book_entries.category
        public const string PaymentModeCash      = "cash";           // cash_book_entries.payment_mode
        public const string RefRiderSettlement   = "rider_settlement"; // cash_book_entries.reference_type
    }

    public async Task<RiderSettlementDto?> Handle(SettleRiderCodCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var rider = await _db.Riders
            .FirstOrDefaultAsync(r => r.Id == cmd.RiderId && r.BrandId == brandId, ct);
        if (rider is null) return null;
        // Franchise scoping: a franchise-scoped actor may only settle their own riders.
        if (_user.FranchiseId is Guid fid && rider.FranchiseId != fid) return null;

        // The store (deposit location), if given, must belong to this brand.
        if (cmd.Request.StoreId is Guid storeId)
        {
            var ok = await _db.Stores.AnyAsync(s => s.Id == storeId && s.BrandId == brandId, ct);
            if (!ok) throw new BusinessRuleException("StoreId does not belong to the current brand.");
        }

        // All of this rider's outstanding collections (tracked → updated below).
        var outstanding = await _db.DeliveryAssignments
            .Where(d => d.BrandId == brandId && d.RiderId == cmd.RiderId
                     && d.CodAmount != null && d.SettlementId == null)
            .ToListAsync(ct);
        if (outstanding.Count == 0) return null;  // nothing to settle

        var now = DateTimeOffset.UtcNow;
        var total = outstanding.Sum(d => d.CodAmount ?? 0m);

        var settlement = new RiderSettlement
        {
            Id = Guid.NewGuid(),
            BrandId = brandId,
            FranchiseId = rider.FranchiseId,
            RiderId = cmd.RiderId,
            StoreId = cmd.Request.StoreId,
            TotalAmount = total,
            CollectionCount = outstanding.Count,
            Reference = cmd.Request.Reference?.Trim(),
            Status = SettlementSettled,
            SettledAt = now,
            SettledBy = cmd.ActorId,
            Notes = cmd.Request.Notes?.Trim(),
            Metadata = "{}",
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = cmd.ActorId,
            UpdatedBy = cmd.ActorId,
        };
        _db.RiderSettlements.Add(settlement);

        foreach (var d in outstanding)
        {
            d.SettlementId = settlement.Id;
            d.UpdatedAt = now;
            d.UpdatedBy = cmd.ActorId;
        }

        // Mirror the handed-over cash into the store's finance cash book (same
        // transaction). Best-effort: only when a store is named AND we can reach an
        // open book for the day — otherwise the settlement still stands on its own.
        await PostCashBookEntryAsync(settlement, brandId, total, now, cmd.ActorId, ct);

        await _db.SaveChangesAsync(ct);

        string? storeName = settlement.StoreId is Guid sid
            ? await _db.Stores.AsNoTracking().Where(s => s.Id == sid).Select(s => s.Name).FirstOrDefaultAsync(ct)
            : null;

        return new RiderSettlementDto(
            settlement.Id, settlement.RiderId, settlement.StoreId, storeName,
            settlement.TotalAmount, settlement.CollectionCount, settlement.Reference,
            settlement.Status, settlement.SettledAt, settlement.SettledBy, settlement.Notes);
    }

    /// <summary>
    /// Adds the settlement's cash to the store's cash book as a cash_in / order_payment
    /// entry and bumps the book's CashInflow. Finds the day's full_day book, opening a
    /// fresh one if none exists; a NO-OP when there's no store or the existing book is
    /// not open. Tracked on the shared context — committed by the caller's SaveChanges.
    /// </summary>
    private async Task PostCashBookEntryAsync(
        RiderSettlement settlement, Guid brandId, decimal total, DateTimeOffset now, Guid? actorId, CancellationToken ct)
    {
        if (total <= 0m) return;
        if (settlement.StoreId is not Guid storeId) return;   // no store → nothing to post against

        var bookDate = DateOnly.FromDateTime(now.UtcDateTime);

        var book = await _db.CashBooks.FirstOrDefaultAsync(
            b => b.BrandId == brandId && b.StoreId == storeId
              && b.BookDate == bookDate && b.ShiftLabel == Cb.ShiftFullDay, ct);

        if (book is null)
        {
            // No book for the day yet — open one (zeroed, status = open).
            book = new CashBook
            {
                Id = Guid.NewGuid(),
                BrandId = brandId,
                FranchiseId = settlement.FranchiseId,
                StoreId = storeId,
                BookDate = bookDate,
                ShiftLabel = Cb.ShiftFullDay,
                OpeningUserId = actorId ?? Guid.Empty,
                OpeningBalance = 0,
                CashInflow = 0,
                CashOutflow = 0,
                UpiInflow = 0,
                CardInflow = 0,
                OtherInflow = 0,
                DepositAmount = 0,
                TotalOrders = 0,
                NewOrders = 0,
                DeliveredOrders = 0,
                CancelledOrders = 0,
                Status = Cb.StatusOpen,
                Metadata = "{}",
                OpenedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = actorId,
                UpdatedBy = actorId,
            };
            _db.CashBooks.Add(book);
        }
        else if (book.Status != Cb.StatusOpen)
        {
            return;   // book already closed/finalized → skip, settlement stands alone
        }

        _db.CashBookEntries.Add(new CashBookEntry
        {
            Id = Guid.NewGuid(),
            CashBookId = book.Id,
            BrandId = brandId,
            StoreId = storeId,
            EntryType = Cb.EntryCashIn,
            Category = Cb.CategoryOrderPayment,
            Direction = 1,
            Amount = total,
            PaymentMode = Cb.PaymentModeCash,
            ReferenceType = Cb.RefRiderSettlement,
            ReferenceId = settlement.Id,
            Description = $"Rider COD settlement ({settlement.CollectionCount} collection(s))",
            PerformedBy = actorId ?? Guid.Empty,
            OccurredAt = now,
            Metadata = "{}",
            CreatedAt = now,
            CreatedBy = actorId,
        });

        // Cash, direction = in → increases the book's cash inflow.
        book.CashInflow += total;
        book.UpdatedAt = now;
        book.UpdatedBy = actorId;
    }
}
