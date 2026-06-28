using core.Application.Common.Interfaces;
using core.Application.Identity.Entitlements.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Entitlements.Queries;

/// <summary>Platform-wide SaaS revenue summary (operator MRR view) — what the platform earns from
/// brands paying for their tiers. Aggregates identity_access.brand_platform_subscription / _invoice.</summary>
public sealed record GetPlatformBillingSummaryQuery : IQuery<PlatformBillingSummaryDto>;

public class GetPlatformBillingSummaryQueryHandler
    : IQueryHandler<GetPlatformBillingSummaryQuery, PlatformBillingSummaryDto>
{
    private readonly ICoreDbContext _db;
    public GetPlatformBillingSummaryQueryHandler(ICoreDbContext db) => _db = db;

    /// <summary>Normalise a tier's billed price to a monthly figure.</summary>
    private static decimal Monthly(decimal price, string interval) => interval switch
    {
        "quarterly"   => price / 3m,
        "half_yearly" => price / 6m,
        "yearly"      => price / 12m,
        _             => price, // monthly
    };

    public async Task<PlatformBillingSummaryDto> HandleAsync(GetPlatformBillingSummaryQuery q, CancellationToken ct)
    {
        var subs = await _db.BrandPlatformSubscriptions.AsNoTracking()
            .Where(s => s.Status == "active")
            .Select(s => new { s.BundleCode, s.PlanName, s.Price, s.BillingInterval, s.CurrencyCode })
            .ToListAsync(ct);

        var byTier = subs
            .GroupBy(s => new { s.BundleCode, s.PlanName })
            .Select(g => new TierMrrDto(
                g.Key.BundleCode, g.Key.PlanName, g.Count(),
                Math.Round(g.Sum(x => Monthly(x.Price, x.BillingInterval)), 2)))
            .OrderByDescending(t => t.MonthlyMrr)
            .ToList();

        var monthlyMrr = Math.Round(byTier.Sum(t => t.MonthlyMrr), 2);
        var currency = subs.Count > 0 ? subs[0].CurrencyCode : "INR";

        var invoicesByStatus = await _db.BrandPlatformInvoices.AsNoTracking()
            .GroupBy(i => i.Status)
            .Select(g => new InvoiceStatusTotalDto(g.Key, g.Count(), g.Sum(x => x.Amount)))
            .ToListAsync(ct);

        decimal Sum(string status) => invoicesByStatus.Where(s => s.Status == status).Sum(s => s.TotalAmount);

        return new PlatformBillingSummaryDto(
            Currency: currency,
            MonthlyMrr: monthlyMrr,
            AnnualRunRate: Math.Round(monthlyMrr * 12m, 2),
            ActiveTenants: subs.Count,
            OutstandingAmount: Sum("issued"),
            CollectedAmount: Sum("paid"),
            ByTier: byTier,
            InvoicesByStatus: invoicesByStatus.OrderBy(s => s.Status).ToList());
    }
}
