using core.Application.Common.Interfaces;
using core.Application.Identity.Entitlements.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Entitlements.Queries;

/// <summary>A brand's platform subscription (the tier it pays for) + its recent invoices,
/// or null if the brand is not on any priced tier yet.</summary>
public sealed record GetBrandPlatformSubscriptionQuery(Guid BrandId) : IQuery<BrandPlatformSubscriptionDto?>;

public class GetBrandPlatformSubscriptionQueryHandler
    : IQueryHandler<GetBrandPlatformSubscriptionQuery, BrandPlatformSubscriptionDto?>
{
    private readonly ICoreDbContext _db;
    public GetBrandPlatformSubscriptionQueryHandler(ICoreDbContext db) => _db = db;

    public async Task<BrandPlatformSubscriptionDto?> HandleAsync(GetBrandPlatformSubscriptionQuery q, CancellationToken ct)
    {
        var sub = await _db.BrandPlatformSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BrandId == q.BrandId, ct);
        if (sub is null) return null;

        var invoices = await _db.BrandPlatformInvoices.AsNoTracking()
            .Where(i => i.SubscriptionId == sub.Id)
            .OrderByDescending(i => i.BillingPeriodStart)
            .Take(12)
            .Select(i => new BrandPlatformInvoiceDto(
                i.Id, i.BillingPeriodStart, i.BillingPeriodEnd, i.Amount, i.CurrencyCode, i.Status, i.IssuedAt, i.DueAt,
                i.PaymentLinkUrl))
            .ToListAsync(ct);

        return new BrandPlatformSubscriptionDto(
            sub.Id, sub.BrandId, sub.BundleCode, sub.PlanName, sub.Price, sub.BillingInterval, sub.CurrencyCode,
            sub.Status, sub.CurrentPeriodStart, sub.CurrentPeriodEnd, sub.NextBillingAt, sub.AutoRenew, invoices);
    }
}
