using laundryghar.Commerce.Application;
using MediatR;

namespace laundryghar.Commerce.Application.Customer.Loyalty;

// ── Get loyalty balance + history ─────────────────────────────────────────────

public sealed record GetMyLoyaltyBalanceQuery(Guid CustomerId, Guid BrandId) : IRequest<LoyaltyBalanceDto?>;

public sealed class GetMyLoyaltyBalanceHandler : IRequestHandler<GetMyLoyaltyBalanceQuery, LoyaltyBalanceDto?>
{
    private readonly LaundryGharDbContext _db;

    public GetMyLoyaltyBalanceHandler(LaundryGharDbContext db) => _db = db;

    public async Task<LoyaltyBalanceDto?> Handle(GetMyLoyaltyBalanceQuery q, CancellationToken ct)
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
