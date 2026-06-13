using System.Text.Json;
using laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Worker.Abstractions;
using laundryghar.Worker.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace laundryghar.Worker.Services;

/// <summary>
/// Daily subscription billing + dunning job (ADR-010).
///
/// Runs once per day. On each cycle it:
///   (a) Generates subscription_invoices for subscriptions whose period ends today.
///       Also writes an 'allocate' row to subscription_usage_ledger to reset quota for the new cycle.
///   (b) Attempts to charge via mandate (ISubscriptionGatewayCharger, or DevStub in Development).
///   (c) Dunning ladder on failure:
///         attempt 1 → status stays 'active', schedule retry at +1×backoff
///         attempt 2 → status → 'past_due', schedule retry at +2×backoff
///         attempt N ≥ MaxDunning → status → 'suspended', emit subscription.suspended outbox event
///       On success at any attempt: status → 'active', schedule next billing, reset dunning counters,
///       emit subscription.renewed outbox event.
///
/// Design mirrors RoyaltyGenerationService:
///   - Opt-in flag: Worker:SubscriptionBillingEnabled (default false).
///   - Worker bypasses RLS — all brands visible.
///   - Per-subscription isolation: one failure does not abort the batch.
///   - Append-only billing_attempts with idempotency_key prevent double-debit.
/// </summary>
public sealed class SubscriptionBillingService : BackgroundService
{
    private readonly IServiceScopeFactory                _scopeFactory;
    private readonly ILogger<SubscriptionBillingService> _logger;
    private readonly WorkerOptions                        _options;
    private readonly ISubscriptionCharger?                _charger;

    public SubscriptionBillingService(
        IServiceScopeFactory                scopeFactory,
        ILogger<SubscriptionBillingService> logger,
        IOptions<WorkerOptions>             options,
        ISubscriptionCharger?               charger = null)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
        _options      = options.Value;
        _charger      = charger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.SubscriptionBillingEnabled)
        {
            _logger.LogInformation(
                "SubscriptionBillingService disabled (Worker:SubscriptionBillingEnabled=false). " +
                "Set to true to enable daily subscription billing and dunning.");
            return;
        }

        _logger.LogInformation(
            "SubscriptionBillingService starting (pollIntervalSeconds={Interval}, maxDunning={MaxDunning}).",
            _options.SubscriptionBillingPollIntervalSeconds,
            _options.SubscriptionMaxDunningAttempts);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "SubscriptionBillingService: unhandled error in cycle; will retry next tick.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.SubscriptionBillingPollIntervalSeconds),
                stoppingToken);
        }

        _logger.LogInformation("SubscriptionBillingService stopped.");
    }

    // ── Main cycle ─────────────────────────────────────────────────────────────

    private async Task RunCycleAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateWorkerAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();

        var now = DateTimeOffset.UtcNow;

        // (a) Generate invoices for subscriptions due today
        await GenerateDueInvoicesAsync(db, now, ct);

        // (b) Attempt charge on all open (draft/issued) invoices — both newly generated
        //     and previously failed retries whose next_retry_at has passed.
        await AttemptPendingChargesAsync(db, now, ct);
    }

    // ── (a) Invoice generation ─────────────────────────────────────────────────

    /// <summary>
    /// Creates one subscription_invoice per billing cycle for every active/trialing
    /// subscription whose current_period_end ≤ now and no invoice already exists for
    /// that period (idempotent).
    /// Also writes an 'allocate' ledger entry to reset quota for the new cycle.
    /// </summary>
    private async Task GenerateDueInvoicesAsync(
        LaundryGharDbContext db,
        DateTimeOffset       now,
        CancellationToken    ct)
    {
        var dueSubscriptions = await db.CustomerSubscriptions
            .Include(cs => cs.Plan)
            .Where(cs => cs.Status            == "active"
                      || cs.Status            == "trialing"
                      || cs.Status            == "past_due"    // retry previous failed invoice
            )
            .Where(cs => cs.AutoRenew == true
                      && cs.CurrentPeriodEnd  != null
                      && cs.CurrentPeriodEnd  <= now)
            .ToListAsync(ct);

        _logger.LogInformation(
            "SubscriptionBillingService: {Count} subscriptions due for billing.",
            dueSubscriptions.Count);

        int generated = 0;
        int skipped   = 0;
        int failed    = 0;

        foreach (var sub in dueSubscriptions)
        {
            try
            {
                var alreadyExists = await db.SubscriptionInvoices.AnyAsync(
                    i => i.CustomerSubscriptionId == sub.Id
                      && i.BillingPeriodStart     == sub.CurrentPeriodStart, ct);

                if (alreadyExists) { skipped++; continue; }

                await GenerateInvoiceForSubscriptionAsync(db, sub, now, ct);
                generated++;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex,
                    "SubscriptionBillingService: failed to generate invoice for subscriptionId={SubId}; skipping.",
                    sub.Id);
            }
        }

        _logger.LogInformation(
            "SubscriptionBillingService: invoice generation — generated={Generated}, skipped={Skipped}, failed={Failed}.",
            generated, skipped, failed);
    }

    private async Task GenerateInvoiceForSubscriptionAsync(
        LaundryGharDbContext  db,
        CustomerSubscription sub,
        DateTimeOffset        now,
        CancellationToken     ct)
    {
        var plan      = sub.Plan;
        var basePrice = sub.PriceSnapshot;

        // Tax calculation — IGST 18% (simplified; CGST/SGST used for intrastate — adjust per config)
        const decimal gstRate  = 18m;
        var taxable            = basePrice;
        var igst               = Math.Round(taxable * gstRate / 100, 2);
        var grandTotal         = taxable + igst;

        var invCount  = await db.SubscriptionInvoices.CountAsync(
            i => i.BrandId == sub.BrandId, ct);
        var invNumber = $"SI-{now:yyyyMMdd}-{(invCount + 1):D6}";

        var invoice = new SubscriptionInvoice
        {
            Id                       = Guid.NewGuid(),
            BrandId                  = sub.BrandId,
            CustomerSubscriptionId   = sub.Id,
            CustomerId               = sub.CustomerId,
            InvoiceNumber            = invNumber,
            BillingPeriodStart       = sub.CurrentPeriodStart!.Value,
            BillingPeriodEnd         = sub.CurrentPeriodEnd!.Value,
            Subtotal                 = basePrice,
            SetupFee                 = 0,
            DiscountTotal            = 0,
            TaxableAmount            = taxable,
            Cgst                     = 0,
            Sgst                     = 0,
            Igst                     = igst,
            TaxTotal                 = igst,
            GrandTotal               = grandTotal,
            AmountPaid               = 0,
            CurrencyCode             = sub.CurrencyCode,
            Status                   = "draft",
            AttemptCount             = 0,
            IssuedAt                 = now,
            DueAt                    = now.AddDays(7),   // 7-day payment window
            Metadata                 = "{}",
            CreatedAt                = now,
            UpdatedAt                = now
        };

        db.SubscriptionInvoices.Add(invoice);

        // Allocate quota for new cycle
        var (newPeriodStart, newPeriodEnd) = ComputeNextPeriod(sub, now);

        if (sub.QuotaType != "unlimited" && sub.QuotaValue.HasValue)
        {
            var rolledOver  = 0m;
            if (plan.RolloverUnused && sub.CreditsRemaining > 0)
            {
                rolledOver = plan.MaxRollover.HasValue
                    ? Math.Min(sub.CreditsRemaining, plan.MaxRollover.Value)
                    : sub.CreditsRemaining;
            }
            var newBalance = sub.QuotaValue.Value + rolledOver;

            db.SubscriptionUsageLedger.Add(new SubscriptionUsageLedger
            {
                Id                       = Guid.NewGuid(),
                BrandId                  = sub.BrandId,
                CustomerSubscriptionId   = sub.Id,
                CustomerId               = sub.CustomerId,
                BillingPeriodStart       = newPeriodStart,
                BillingPeriodEnd         = newPeriodEnd,
                TransactionType          = "allocate",
                Amount                   = sub.QuotaValue.Value,
                BalanceBefore            = sub.CreditsRemaining,
                BalanceAfter             = newBalance,
                Notes                    = $"Cycle allocation for period {newPeriodStart:yyyy-MM-dd} to {newPeriodEnd:yyyy-MM-dd}",
                PerformedByType          = "system",
                OccurredAt               = now
            });

            if (rolledOver > 0)
            {
                db.SubscriptionUsageLedger.Add(new SubscriptionUsageLedger
                {
                    Id                     = Guid.NewGuid(),
                    BrandId                = sub.BrandId,
                    CustomerSubscriptionId = sub.Id,
                    CustomerId             = sub.CustomerId,
                    BillingPeriodStart     = newPeriodStart,
                    BillingPeriodEnd       = newPeriodEnd,
                    TransactionType        = "rollover",
                    Amount                 = rolledOver,
                    BalanceBefore          = sub.QuotaValue.Value,
                    BalanceAfter           = newBalance,
                    Notes                  = $"Rolled over {rolledOver} unused quota from previous cycle.",
                    PerformedByType        = "system",
                    OccurredAt             = now
                });
            }

            sub.CreditsRemaining = newBalance;
        }

        // Advance period pointers (will be confirmed after successful payment)
        sub.CurrentPeriodStart = newPeriodStart;
        sub.CurrentPeriodEnd   = newPeriodEnd;
        sub.NextBillingAt      = newPeriodEnd;
        sub.UpdatedAt          = now;
        sub.Version++;

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SubscriptionBillingService: generated invoice {InvoiceNumber} for subscriptionId={SubId}.",
            invNumber, sub.Id);
    }

    // ── (b) Charge attempts ────────────────────────────────────────────────────

    /// <summary>
    /// Processes all draft/issued invoices whose retry window has passed.
    /// Each attempt is isolated: failure of one does not block others.
    /// </summary>
    private async Task AttemptPendingChargesAsync(
        LaundryGharDbContext db,
        DateTimeOffset       now,
        CancellationToken    ct)
    {
        var pendingInvoices = await db.SubscriptionInvoices
            .Include(i => i.CustomerSubscription)
                .ThenInclude(cs => cs.Mandate)
            .Where(i => (i.Status == "draft" || i.Status == "issued" || i.Status == "past_due")
                     && i.AmountDue > 0
                     && i.CustomerSubscription.AutoRenew == true
                     && (i.CustomerSubscription.Status == "active"
                      || i.CustomerSubscription.Status == "trialing"
                      || i.CustomerSubscription.Status == "past_due"))
            .ToListAsync(ct);

        // Filter to invoices that are ready for retry (check via billing_attempts)
        var invoiceIds  = pendingInvoices.Select(i => i.Id).ToList();
        var lastAttempts = await db.SubscriptionBillingAttempts
            .Where(a => invoiceIds.Contains(a.SubscriptionInvoiceId))
            .GroupBy(a => a.SubscriptionInvoiceId)
            .Select(g => new { InvoiceId = g.Key, LatestRetryAt = g.Max(a => a.NextRetryAt) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.LatestRetryAt, ct);

        int charged = 0; int failed = 0;

        foreach (var invoice in pendingInvoices)
        {
            // Skip if next_retry_at is in the future
            if (lastAttempts.TryGetValue(invoice.Id, out var nextRetry)
                && nextRetry.HasValue && nextRetry.Value > now)
                continue;

            try
            {
                await AttemptChargeAsync(db, invoice, now, ct);
                charged++;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex,
                    "SubscriptionBillingService: unexpected error attempting charge for invoiceId={InvId}; skipping.",
                    invoice.Id);
            }
        }

        _logger.LogInformation(
            "SubscriptionBillingService: charge attempts — processed={Charged}, errors={Failed}.",
            charged, failed);
    }

    private async Task AttemptChargeAsync(
        LaundryGharDbContext  db,
        SubscriptionInvoice  invoice,
        DateTimeOffset        now,
        CancellationToken     ct)
    {
        var sub     = invoice.CustomerSubscription;
        var mandate = sub.Mandate;

        // Idempotency key: invoice + attempt number
        var nextAttemptNo = (short)(invoice.AttemptCount + 1);
        var idemKey = $"subcharge-{invoice.Id:N}-{nextAttemptNo}";

        // Dev stub: simulate success via config; production uses gateway
        // The actual gateway charge is done by the gateway seam; the Worker does NOT
        // depend on Commerce.Infrastructure.Gateway directly (to avoid cross-assembly
        // coupling). Instead it uses a minimal ISubscriptionCharger abstraction
        // that is resolved from DI. In this implementation we use a stub that
        // reads from config, mirroring how other services fail-safe in Development.
        var chargeResult = await SimulateOrChargeAsync(db, sub, mandate, invoice, idemKey, now, ct);

        // Record the attempt row (append-only)
        var attempt = new SubscriptionBillingAttempt
        {
            Id                       = Guid.NewGuid(),
            BrandId                  = invoice.BrandId,
            CustomerSubscriptionId   = sub.Id,
            SubscriptionInvoiceId    = invoice.Id,
            MandateId                = mandate?.Id,
            AttemptNumber            = nextAttemptNo,
            Amount                   = invoice.GrandTotal,
            Gateway                  = mandate?.Gateway ?? "system",
            GatewayPaymentId         = chargeResult.GatewayPaymentId,
            Status                   = chargeResult.Status,
            FailureCode              = chargeResult.FailureCode,
            FailureMessage           = chargeResult.FailureMessage,
            AttemptedAt              = now,
            NextRetryAt              = chargeResult.Status == "failed"
                ? now.AddMinutes(nextAttemptNo * _options.SubscriptionDunningBackoffMinutes)
                : null,
            IdempotencyKey           = idemKey,
            CreatedAt                = now
        };
        db.SubscriptionBillingAttempts.Add(attempt);

        // Update invoice + subscription state + emit outbox event
        invoice.AttemptCount++;
        invoice.UpdatedAt = now;

        if (chargeResult.Status == "success")
        {
            invoice.Status    = "paid";
            invoice.PaidAt    = now;
            invoice.AmountPaid = invoice.GrandTotal;
            invoice.UpdatedAt = now;

            sub.Status               = "active";
            sub.PastDueSince         = null;
            sub.DunningAttempts      = 0;
            sub.FailedPaymentCount   = 0;
            sub.TotalCyclesBilled++;
            sub.ActivatedAt          ??= now;
            sub.UpdatedAt            = now;
            sub.Version++;

            EmitOutboxEvent(db, "subscription.renewed", sub.BrandId, sub.Id, invoice, now);
        }
        else
        {
            // Failure path — advance dunning state
            sub.FailedPaymentCount++;
            sub.DunningAttempts++;
            sub.UpdatedAt = now;
            sub.Version++;

            if (sub.DunningAttempts >= _options.SubscriptionMaxDunningAttempts)
            {
                sub.Status     = "suspended";
                invoice.Status = "failed";
                EmitOutboxEvent(db, "subscription.suspended", sub.BrandId, sub.Id, invoice, now);
                _logger.LogWarning(
                    "SubscriptionBillingService: subscriptionId={SubId} suspended after {Attempts} dunning attempts.",
                    sub.Id, sub.DunningAttempts);
            }
            else if (sub.DunningAttempts == 1)
            {
                // First failure: move to past_due
                sub.Status       = "past_due";
                sub.PastDueSince = now;
                invoice.Status   = "past_due";
                EmitOutboxEvent(db, "subscription.past_due", sub.BrandId, sub.Id, invoice, now);
            }
            // else: already past_due, retry scheduled via next_retry_at
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Gateway charge seam ────────────────────────────────────────────────────
    // Delegates to ISubscriptionCharger when one is registered:
    //   - Development: DevSubscriptionCharger (registered in Program.cs) simulates success.
    //   - Production:  register a real gateway implementation; no charger = fail-closed.
    //
    // Fail-closed contract: if no ISubscriptionCharger is registered in the DI container,
    // the method returns a "failed" result with code "no_charger_configured". This ensures
    // no phantom "paid" invoices can appear in Production due to a misconfigured deployment.

    private async Task<ChargeAttemptResult> SimulateOrChargeAsync(
        LaundryGharDbContext  db,
        CustomerSubscription sub,
        PaymentMandate?       mandate,
        SubscriptionInvoice  invoice,
        string               idemKey,
        DateTimeOffset        now,
        CancellationToken     ct)
    {
        if (_charger is null)
        {
            _logger.LogError(
                "SubscriptionBillingService: no ISubscriptionCharger registered. " +
                "Marking invoiceId={InvId} as failed. " +
                "Register a real charger for Production or DevSubscriptionCharger for Development.",
                invoice.Id);
            return new ChargeAttemptResult(
                GatewayPaymentId: "no_charger",
                Status:           "failed",
                FailureCode:      "no_charger_configured",
                FailureMessage:   "No ISubscriptionCharger is registered in this environment.");
        }

        var r = await _charger.ChargeAsync(sub, mandate, invoice, idemKey, ct);
        return new ChargeAttemptResult(r.GatewayPaymentId, r.Status, r.FailureCode, r.FailureMessage);
    }

    // ── Outbox events ─────────────────────────────────────────────────────────

    private static void EmitOutboxEvent(
        LaundryGharDbContext db,
        string               eventType,
        Guid                 brandId,
        Guid                 subscriptionId,
        SubscriptionInvoice  invoice,
        DateTimeOffset        now)
    {
        var payload = JsonSerializer.Serialize(new
        {
            SubscriptionId = subscriptionId,
            InvoiceId      = invoice.Id,
            InvoiceNumber  = invoice.InvoiceNumber,
            BrandId        = brandId,
            CustomerId     = invoice.CustomerId,
            GrandTotal     = invoice.GrandTotal,
            Status         = invoice.Status,
            OccurredAt     = now,
            Source         = "worker_subscription_billing"
        });

        db.OutboxEvents.Add(new OutboxEvent
        {
            Id            = Guid.NewGuid(),
            BrandId       = brandId,
            AggregateType = "customer_subscription",
            AggregateId   = subscriptionId,
            EventType     = eventType,
            EventVersion  = 1,
            Payload       = payload,
            Metadata      = "{}",
            OccurredAt    = now,
            Status        = "pending",
            CreatedAt     = now,
            CreatedBy     = null
        });
    }

    // ── Period math ────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the next billing period based on the subscription's billing_interval
    /// and interval_count. Used to advance current_period_start/end on renewal.
    /// Public for unit testing.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End) ComputeNextPeriod(
        CustomerSubscription sub,
        DateTimeOffset        now)
    {
        var intervalCount = sub.IntervalCount > 0 ? sub.IntervalCount : 1;
        var periodStart   = sub.CurrentPeriodEnd ?? now;

        var periodEnd = sub.BillingInterval switch
        {
            "weekly"      => periodStart.AddDays(7  * intervalCount),
            "monthly"     => periodStart.AddMonths(1 * intervalCount),
            "quarterly"   => periodStart.AddMonths(3 * intervalCount),
            "half_yearly" => periodStart.AddMonths(6 * intervalCount),
            "yearly"      => periodStart.AddYears(1  * intervalCount),
            _             => periodStart.AddMonths(1)
        };

        return (periodStart, periodEnd);
    }

    // ── Private record ────────────────────────────────────────────────────────

    private sealed record ChargeAttemptResult(
        string  GatewayPaymentId,
        string  Status,
        string? FailureCode,
        string? FailureMessage);
}
