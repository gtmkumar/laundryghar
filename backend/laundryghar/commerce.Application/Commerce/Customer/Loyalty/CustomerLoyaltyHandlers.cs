using commerce.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Commerce.Customer.Loyalty;

// ── Get loyalty balance + history ─────────────────────────────────────────────

public sealed record GetMyLoyaltyBalanceQuery(Guid CustomerId, Guid BrandId) : IQuery<LoyaltyBalanceDto?>;

public sealed class GetMyLoyaltyBalanceHandler : IQueryHandler<GetMyLoyaltyBalanceQuery, LoyaltyBalanceDto?>
{
    private readonly ICommerceDbContext _db;

    public GetMyLoyaltyBalanceHandler(ICommerceDbContext db) => _db = db;

    public async Task<LoyaltyBalanceDto?> HandleAsync(GetMyLoyaltyBalanceQuery q, CancellationToken ct)
    {
        // Verify customer exists (self-filter guard)
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == q.CustomerId && c.BrandId == q.BrandId, ct);
        if (customer is null) return null;

        var history = await _db.LoyaltyPointsLedger
            .Where(x => x.CustomerId == q.CustomerId && x.BrandId == q.BrandId)
            .OrderByDescending(x => x.OccurredAt)
            .Take(50)
            .Select(x => new LoyaltyPointsLedgerDto(
                x.Id, x.BrandId, x.CustomerId, x.LoyaltyProgramId,
                x.TransactionType, x.Direction, x.Points,
                x.BalanceBefore, x.BalanceAfter, x.MonetaryEquivalent,
                x.ReferenceType, x.ReferenceId, x.Notes, x.OccurredAt, x.CreatedAt))
            .ToListAsync(ct);

        return new LoyaltyBalanceDto(
            q.CustomerId,
            customer.LoyaltyPointsBalance,
            history);
    }
}
