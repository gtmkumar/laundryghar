using commerce.Infrastructure.Worker.Options;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace commerce.Infrastructure.Worker.Services;

/// <summary>
/// Recurring billing for BRAND platform subscriptions (the tenant's own platform tier).
///
/// Each cycle, for every active auto-renewing subscription whose current period has ended, it rolls
/// the period forward and issues a <c>brand_platform_invoice</c> for the new period (idempotent on
/// (subscription, period)). The first invoice is issued at tier-apply time by ApplyBundleToBrand;
/// this worker handles RENEWALS. Invoices are left <c>issued</c> — actual gateway charging is the
/// deferred P0 item. Uses the full LaundryGharDbContext (RLS-bypassed, all brands), like the other
/// billing workers. Opt-in via <c>Worker:BrandPlatformBillingEnabled</c>.
/// </summary>
public sealed class BrandPlatformBillingService : BackgroundService
{
    private readonly IServiceScopeFactory                 _scopeFactory;
    private readonly ILogger<BrandPlatformBillingService> _logger;
    private readonly WorkerOptions                         _options;

    public BrandPlatformBillingService(
        IServiceScopeFactory                 scopeFactory,
        ILogger<BrandPlatformBillingService> logger,
        IOptions<WorkerOptions>              options)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
        _options      = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.BrandPlatformBillingEnabled)
        {
            _logger.LogInformation(
                "BrandPlatformBillingService disabled (Worker:BrandPlatformBillingEnabled=false).");
            return;
        }

        _logger.LogInformation("BrandPlatformBillingService starting (pollIntervalSeconds={Interval}).",
            _options.BrandPlatformBillingPollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunCycleAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "BrandPlatformBillingService: cycle error; will retry."); }

            await Task.Delay(TimeSpan.FromSeconds(_options.BrandPlatformBillingPollIntervalSeconds), stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();
        var now = DateTimeOffset.UtcNow;

        var due = await db.BrandPlatformSubscriptions
            .Where(s => s.Status == "active" && s.AutoRenew && s.CurrentPeriodEnd <= now)
            .ToListAsync(ct);

        // Batched per chunk (not per subscription, not all-at-once): no external gateway call and
        // no shared numbering scheme couples one subscription's write to another's, so a chunk-wide
        // save is safe. Chunking (not a single SaveChangesAsync for all of `due`) bounds the blast
        // radius of one bad subscription to its chunk and avoids an oversized statement/long lock
        // when the platform has many brands. A failed chunk is retried whole on the next poll cycle
        // (idempotent via the per-period BrandPlatformInvoices.AnyAsync check below) rather than
        // partially — simpler than picking apart a shared change tracker after a mid-chunk exception.
        const int ChunkSize = 50;
        int issued = 0;
        foreach (var chunk in due.Chunk(ChunkSize))
        {
            var chunkIssued = 0;
            try
            {
                foreach (var sub in chunk)
                {
                    // Roll forward one period at a time until caught up to now (cap to avoid runaway).
                    for (var guard = 0; guard < 120 && sub.CurrentPeriodEnd <= now; guard++)
                    {
                        var nextStart = sub.CurrentPeriodEnd;
                        var nextEnd   = AddInterval(nextStart, sub.BillingInterval);
                        sub.CurrentPeriodStart = nextStart;
                        sub.CurrentPeriodEnd   = nextEnd;
                        sub.NextBillingAt      = nextEnd;
                        sub.UpdatedAt          = now;

                        var exists = await db.BrandPlatformInvoices
                            .AnyAsync(i => i.SubscriptionId == sub.Id && i.BillingPeriodStart == nextStart, ct);
                        if (!exists)
                        {
                            db.BrandPlatformInvoices.Add(new BrandPlatformInvoice
                            {
                                Id = Guid.NewGuid(), SubscriptionId = sub.Id, BrandId = sub.BrandId,
                                BillingPeriodStart = nextStart, BillingPeriodEnd = nextEnd,
                                Amount = sub.Price, CurrencyCode = sub.CurrencyCode, Status = "issued",
                                IssuedAt = now, DueAt = now.AddDays(7), CreatedAt = now,
                            });
                            chunkIssued++;
                        }
                    }
                }
                await db.SaveChangesAsync(ct);
                issued += chunkIssued; // only counted once the chunk actually persisted
            }
            catch (Exception ex)
            {
                // Chunk-wide failure: discard this chunk's pending changes so the next chunk starts
                // clean, and let the next poll cycle retry these subscriptions from scratch.
                db.ChangeTracker.Clear();
                _logger.LogError(ex,
                    "BrandPlatformBillingService: failed to bill a chunk of {Count} subscription(s) " +
                    "(ids={SubIds}); will retry next cycle.",
                    chunk.Length, string.Join(",", chunk.Select(s => s.Id)));
            }
        }

        if (issued > 0)
            _logger.LogInformation("BrandPlatformBillingService: issued {Count} renewal invoice(s).", issued);
    }

    private static DateTimeOffset AddInterval(DateTimeOffset from, string interval) => interval switch
    {
        "quarterly"   => from.AddMonths(3),
        "half_yearly" => from.AddMonths(6),
        "yearly"      => from.AddMonths(12),
        _             => from.AddMonths(1),
    };
}
