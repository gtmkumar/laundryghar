using laundryghar.SharedDataModel.Entities.Logistics;
using MediatR;

namespace laundryghar.Logistics.Application.RiderCod;

/// <summary>
/// Record a settlement that clears ALL of a rider's outstanding COD cash: creates a
/// rider_settlements row and stamps its id onto every covered delivery leg. Admin-
/// recorded, one step (status = settled). Returns null → 404 (rider not in scope /
/// nothing outstanding). Posting to the finance cash book is a deliberate follow-up.
/// </summary>
public sealed record SettleRiderCodCommand(Guid RiderId, SettleRiderCodRequest Request, Guid? ActorId)
    : IRequest<RiderSettlementDto?>;

public sealed class SettleRiderCodHandler : IRequestHandler<SettleRiderCodCommand, RiderSettlementDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public SettleRiderCodHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

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

        var now   = DateTimeOffset.UtcNow;
        var total = outstanding.Sum(d => d.CodAmount ?? 0m);

        var settlement = new RiderSettlement
        {
            Id              = Guid.NewGuid(),
            BrandId         = brandId,
            FranchiseId     = rider.FranchiseId,
            RiderId         = cmd.RiderId,
            StoreId         = cmd.Request.StoreId,
            TotalAmount     = total,
            CollectionCount = outstanding.Count,
            Reference       = cmd.Request.Reference?.Trim(),
            Status          = "settled",
            SettledAt       = now,
            SettledBy       = cmd.ActorId,
            Notes           = cmd.Request.Notes?.Trim(),
            Metadata        = "{}",
            CreatedAt       = now,
            UpdatedAt       = now,
            CreatedBy       = cmd.ActorId,
            UpdatedBy       = cmd.ActorId,
        };
        _db.RiderSettlements.Add(settlement);

        foreach (var d in outstanding)
        {
            d.SettlementId = settlement.Id;
            d.UpdatedAt    = now;
            d.UpdatedBy    = cmd.ActorId;
        }

        await _db.SaveChangesAsync(ct);

        string? storeName = settlement.StoreId is Guid sid
            ? await _db.Stores.AsNoTracking().Where(s => s.Id == sid).Select(s => s.Name).FirstOrDefaultAsync(ct)
            : null;

        return new RiderSettlementDto(
            settlement.Id, settlement.RiderId, settlement.StoreId, storeName,
            settlement.TotalAmount, settlement.CollectionCount, settlement.Reference,
            settlement.Status, settlement.SettledAt, settlement.SettledBy, settlement.Notes);
    }
}
