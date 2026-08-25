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
        await RunDunningAsync(ct);
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

    /// <summary>
    /// §9's `Active → PastDue → Suspended → Active` edge for the COMPANY's subscription to us.
    ///
    /// <para>The suspension gate (<c>BrandSuspensionMiddleware</c>) has existed and been tested
    /// since T-18. What was missing was anything that pulled the trigger: nothing in the codebase
    /// wrote <c>brands.status = 'suspended'</c>, so a provider could stop paying and keep trading
    /// indefinitely. §5 describes suspend-on-nonpay as already built, and it was — for CUSTOMER
    /// subscriptions, a different engine entirely.</para>
    ///
    /// <para>Three passes, in this order and for a reason. Payments are honoured FIRST, so a company
    /// that paid this morning is never suspended this afternoon by a stale invoice — being wrongly
    /// suspended is far more damaging than being suspended an hour late.</para>
    /// </summary>
    private async Task RunDunningAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateWorkerAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();
            var now = DateTimeOffset.UtcNow;

            // ── 1. Payment recovered → reinstate ────────────────────────────────────────────────
            // Only brands with NO outstanding invoice, and only ones dunning itself suspended:
            // kernel.set_brand_suspension refuses to clear a ToS or manual suspension, so a
            // fraudster settling a bill cannot reinstate themselves.
            var recovered = await db.Database.SqlQuery<Guid>($"""
                SELECT DISTINCT b.id AS "Value"
                FROM   tenancy_org.brands b
                WHERE  b.status = 'suspended'
                  AND  b.suspension_reason = 'nonpayment'
                  AND  NOT EXISTS (
                         SELECT 1 FROM identity_access.brand_platform_invoice i
                         WHERE  i.brand_id = b.id
                           AND  i.status IN ('issued', 'past_due')
                           AND  i.due_at <= now())
                """).ToListAsync(ct);

            foreach (var brandId in recovered)
            {
                await db.Database.ExecuteSqlAsync(
                    $"SELECT kernel.set_brand_suspension({brandId}, false, 'nonpayment')", ct);
                _logger.LogInformation(
                    "Dunning: brand {BrandId} paid up — reinstated to active.", brandId);
            }

            // ── 2. Overdue → PastDue, and count the attempt ─────────────────────────────────────
            var backoff = TimeSpan.FromMinutes(_options.SubscriptionDunningBackoffMinutes);
            var marked = await db.Database.ExecuteSqlAsync($"""
                UPDATE identity_access.brand_platform_invoice
                   SET status          = 'past_due',
                       attempt_count   = attempt_count + 1,
                       last_attempt_at = now(),
                       next_attempt_at = now() + {backoff}
                 WHERE status IN ('issued', 'past_due')
                   AND due_at <= now()
                   AND (next_attempt_at IS NULL OR next_attempt_at <= now())
                """, ct);

            if (marked > 0)
            {
                // The subscription follows its invoices, so the state machine in §9 is readable
                // from the subscription row alone.
                await db.Database.ExecuteSqlAsync($"""
                    UPDATE identity_access.brand_platform_subscription s
                       SET status = 'past_due', updated_at = now()
                     WHERE s.status IN ('active', 'trialing')
                       AND EXISTS (SELECT 1 FROM identity_access.brand_platform_invoice i
                                   WHERE i.subscription_id = s.id AND i.status = 'past_due')
                    """, ct);

                _logger.LogWarning("Dunning: {Count} brand invoice(s) moved to past_due.", marked);
            }

            // ── 3. Grace window elapsed → suspend ───────────────────────────────────────────────
            // BOTH conditions: the retries are exhausted AND the grace window has passed. Either
            // alone would suspend too eagerly — a card that fails three times in an afternoon is a
            // bank problem, not an abandoned account.
            var grace = TimeSpan.FromDays(_options.BrandDunningGraceDays);
            var attempts = _options.BrandMaxDunningAttempts;

            var due = await db.Database.SqlQuery<Guid>($"""
                SELECT DISTINCT i.brand_id AS "Value"
                FROM   identity_access.brand_platform_invoice i
                JOIN   tenancy_org.brands b ON b.id = i.brand_id
                WHERE  i.status = 'past_due'
                  AND  i.attempt_count >= {attempts}
                  AND  i.due_at <= now() - {grace}
                  AND  b.status = 'active'
                """).ToListAsync(ct);

            foreach (var brandId in due)
            {
                var result = await db.Database.SqlQuery<string?>(
                    $"SELECT kernel.set_brand_suspension({brandId}, true, 'nonpayment') AS \"Value\"")
                    .ToListAsync(ct);

                _logger.LogWarning(
                    "Dunning: brand {BrandId} suspended for non-payment after {Attempts} attempts "
                    + "and a {Grace}-day grace window — login, billing and export stay open (§9). "
                    + "Result: {Result}",
                    brandId, attempts, _options.BrandDunningGraceDays,
                    result.Count > 0 ? result[0] : "(none)");
            }
        }
        catch (Exception ex)
        {
            // Never let dunning take the billing cycle down with it. Failing to suspend costs a few
            // days of unpaid usage; failing to INVOICE costs the month.
            _logger.LogError(ex, "Dunning: pass failed; will retry next cycle.");
        }
    }
}
