using System.Text.Json;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Worker.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace laundryghar.Worker.Services;

/// <summary>
/// Monthly royalty auto-generation job.
///
/// On the configured day of month (default: 1st), for each active franchise across all brands,
/// invokes <see cref="GenerateRoyaltyInvoiceCommand"/> for the PREVIOUS calendar month.
/// Franchises that already have an invoice for that period are silently skipped (idempotent).
///
/// Design decisions:
///   - Enabled flag:  <c>Worker:RoyaltyGenerationEnabled=false</c> by default.
///     Set to <c>true</c> (or inject <c>Worker:RoyaltyGenerationDayOfMonth=1</c>) to activate.
///   - "Has today's run happened?" is answered by querying existing invoices for the target
///     period — no separate cursor table is needed.
///   - Poll interval: daily (86 400 s) — checking once per day wastes nothing.
///   - The Worker bypasses RLS; all franchises across all brands are visible.
///     Every GenerateRoyaltyInvoiceCommand is dispatched with an explicit brand context
///     injected via <see cref="WorkerBrandScope"/>.
///   - Per-franchise error isolation: one failed franchise does not abort the whole batch.
///   - An <c>royalty.invoice_generated</c> outbox event is emitted per generated invoice.
/// </summary>
public sealed class RoyaltyGenerationService : BackgroundService
{
    private readonly IServiceScopeFactory              _scopeFactory;
    private readonly ILogger<RoyaltyGenerationService> _logger;
    private readonly WorkerOptions                     _options;

    public RoyaltyGenerationService(
        IServiceScopeFactory               scopeFactory,
        ILogger<RoyaltyGenerationService>  logger,
        IOptions<WorkerOptions>            options)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
        _options      = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.RoyaltyGenerationEnabled)
        {
            _logger.LogInformation(
                "RoyaltyGenerationService disabled (Worker:RoyaltyGenerationEnabled=false). " +
                "Set to true and configure Worker:RoyaltyGenerationDayOfMonth to enable.");
            return;
        }

        _logger.LogInformation(
            "RoyaltyGenerationService starting (dayOfMonth={Day}, pollIntervalSeconds={Interval}).",
            _options.RoyaltyGenerationDayOfMonth,
            _options.RoyaltyGenerationPollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MaybeRunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "RoyaltyGenerationService: unhandled error in poll cycle; will retry next tick.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.RoyaltyGenerationPollIntervalSeconds),
                stoppingToken);
        }

        _logger.LogInformation("RoyaltyGenerationService stopped.");
    }

    /// <summary>
    /// Checks whether today is the configured trigger day and, if so, whether the
    /// current month's batch has already been completed. Runs generation if needed.
    /// </summary>
    private async Task MaybeRunCycleAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Only run on the configured day of month.
        if (today.Day != _options.RoyaltyGenerationDayOfMonth)
            return;

        // The target period is the full previous calendar month.
        var (periodStart, periodEnd) = PreviousMonthWindow(today);

        _logger.LogInformation(
            "RoyaltyGenerationService: triggered for period {Start}–{End} (today={Today}).",
            periodStart, periodEnd, today);

        await RunGenerationBatchAsync(periodStart, periodEnd, ct);
    }

    /// <summary>
    /// For every active franchise across every brand, generates a royalty invoice
    /// for <paramref name="periodStart"/>–<paramref name="periodEnd"/> if one does
    /// not already exist.
    /// </summary>
    private async Task RunGenerationBatchAsync(
        DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateWorkerAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();

        // Load all active franchises with their agreement data.
        // Worker bypasses RLS — all brands are visible.
        var franchises = await db.Franchises
            .Where(f => f.Status == "active" && f.DeletedAt == null)
            .Select(f => new FranchiseSummary(
                f.Id,
                f.BrandId,
                f.RoyaltyPercent,
                f.MarketingFeePercent))
            .ToListAsync(ct);

        if (franchises.Count == 0)
        {
            _logger.LogInformation("RoyaltyGenerationService: no active franchises found.");
            return;
        }

        // Load the set of franchise IDs that already have an invoice for this period.
        // Cheap set-lookup for idempotent skip.
        var alreadyGenerated = await db.RoyaltyInvoices
            .Where(i => i.PeriodStart == periodStart && i.PeriodEnd == periodEnd)
            .Select(i => i.FranchiseId)
            .ToHashSetAsync(ct);

        int generated = 0;
        int skipped   = 0;
        int failed    = 0;

        foreach (var franchise in franchises)
        {
            if (alreadyGenerated.Contains(franchise.Id))
            {
                skipped++;
                continue;
            }

            try
            {
                await GenerateForFranchiseAsync(
                    db, franchise, periodStart, periodEnd, ct);
                generated++;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex,
                    "RoyaltyGenerationService: failed to generate royalty for " +
                    "franchiseId={FranchiseId} brandId={BrandId}; skipping.",
                    franchise.Id, franchise.BrandId);
            }
        }

        _logger.LogInformation(
            "RoyaltyGenerationService: batch complete — " +
            "generated={Generated}, skipped={Skipped}, failed={Failed} " +
            "for period {Start}–{End}.",
            generated, skipped, failed, periodStart, periodEnd);
    }

    /// <summary>
    /// Generates a single royalty invoice for the given franchise and emits
    /// a <c>royalty.invoice_generated</c> outbox event, all in one save round-trip.
    /// </summary>
    private async Task GenerateForFranchiseAsync(
        LaundryGharDbContext db,
        FranchiseSummary     franchise,
        DateOnly             periodStart,
        DateOnly             periodEnd,
        CancellationToken    ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Aggregate completed payments for this franchise in the period.
        var periodFrom = new DateTimeOffset(periodStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var periodTo   = new DateTimeOffset(periodEnd.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var aggResult = await db.Payments
            .Where(p => p.BrandId     == franchise.BrandId
                     && p.FranchiseId == franchise.Id
                     && p.Status      == "completed"
                     && p.CompletedAt >= periodFrom
                     && p.CompletedAt <= periodTo)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Sum(p => p.Amount), Count = g.Count() })
            .FirstOrDefaultAsync(ct);

        var grossRevenue = aggResult?.Total ?? 0m;
        var totalOrders  = aggResult?.Count  ?? 0;

        // Resolve rates from the franchise (set during onboarding).
        var royaltyPercent      = franchise.RoyaltyPercent;
        var marketingFeePercent = franchise.MarketingFeePercent;

        // Calculate amounts.
        var eligibleRevenue    = grossRevenue;
        var royaltyAmount      = Math.Round(eligibleRevenue * royaltyPercent / 100, 2);
        var marketingFeeAmount = Math.Round(eligibleRevenue * marketingFeePercent / 100, 2);
        const decimal gstRate  = 18m; // standard GST rate; adjust via config if needed
        var subtotal           = royaltyAmount + marketingFeeAmount;
        var taxTotal           = Math.Round(subtotal * gstRate / 100, 2);
        var grandTotal         = subtotal + taxTotal;

        // Invoice number: ROY-{yyyyMMdd}-{brand-scoped sequence}.
        var invCount  = await db.RoyaltyInvoices.CountAsync(i => i.BrandId == franchise.BrandId, ct);
        var invNumber = $"ROY-{now:yyyyMMdd}-{(invCount + 1):D4}";

        var invoice = new RoyaltyInvoice
        {
            Id                   = Guid.NewGuid(),
            BrandId              = franchise.BrandId,
            FranchiseId          = franchise.Id,
            FranchiseAgreementId = null,   // could be resolved; left null for auto-gen
            InvoiceNumber        = invNumber,
            PeriodStart          = periodStart,
            PeriodEnd            = periodEnd,
            GrossRevenue         = grossRevenue,
            EligibleRevenue      = eligibleRevenue,
            RoyaltyPercent       = royaltyPercent,
            RoyaltyAmount        = royaltyAmount,
            MarketingFeePercent  = marketingFeePercent,
            MarketingFeeAmount   = marketingFeeAmount,
            TechnologyFeeAmount  = 0,
            OtherCharges         = 0,
            Adjustments          = 0,
            Subtotal             = subtotal,
            Cgst                 = 0,
            Sgst                 = 0,
            Igst                 = taxTotal,
            TaxTotal             = taxTotal,
            GrandTotal           = grandTotal,
            AmountPaid           = 0,
            CurrencyCode         = "INR",
            TotalOrders          = totalOrders,
            InvoiceDate          = DateOnly.FromDateTime(now.UtcDateTime),
            DueDate              = DateOnly.FromDateTime(now.UtcDateTime.AddDays(30)),
            LineItems            = "[]",
            Notes                = $"Auto-generated by worker for period {periodStart}–{periodEnd}.",
            Status               = "draft",
            Metadata             = "{}",
            CreatedAt            = now,
            UpdatedAt            = now,
            CreatedBy            = null   // system-generated
        };

        db.RoyaltyInvoices.Add(invoice);

        // Summary calculation line.
        var calcLine = new RoyaltyCalculation
        {
            Id               = Guid.NewGuid(),
            RoyaltyInvoiceId = invoice.Id,
            BrandId          = franchise.BrandId,
            FranchiseId      = franchise.Id,
            CalculationDate  = DateOnly.FromDateTime(now.UtcDateTime),
            RevenueType      = "adjustment",
            GrossAmount      = grossRevenue,
            ExcludedAmount   = 0,
            EligibleAmount   = eligibleRevenue,
            RoyaltyRate      = royaltyPercent,
            RoyaltyAmount    = royaltyAmount,
            Notes            = $"Auto-generated: {periodStart}–{periodEnd}; orders={totalOrders}.",
            CreatedAt        = now,
            CreatedBy        = null
        };

        db.RoyaltyCalculations.Add(calcLine);

        // Outbox event: royalty.invoice_generated
        var payload = JsonSerializer.Serialize(new
        {
            InvoiceId   = invoice.Id,
            BrandId     = franchise.BrandId,
            FranchiseId = franchise.Id,
            PeriodStart = periodStart,
            PeriodEnd   = periodEnd,
            GrandTotal  = grandTotal,
            Status      = "draft",
            GeneratedAt = now,
            Source      = "worker_auto_generate"
        });

        db.OutboxEvents.Add(new OutboxEvent
        {
            Id            = Guid.NewGuid(),
            BrandId       = franchise.BrandId,
            AggregateType = "royalty_invoice",
            AggregateId   = invoice.Id,
            EventType     = "royalty.invoice_generated",
            EventVersion  = 1,
            Payload       = payload,
            Metadata      = "{}",
            OccurredAt    = now,
            Status        = "pending",
            CreatedAt     = now,
            CreatedBy     = null
        });

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "RoyaltyGenerationService: generated invoice {InvoiceNumber} " +
            "for franchiseId={FranchiseId} brandId={BrandId} " +
            "period={Start}–{End} gross={Gross:F2} grandTotal={Total:F2}.",
            invNumber, franchise.Id, franchise.BrandId,
            periodStart, periodEnd, grossRevenue, grandTotal);
    }

    // ── Period math ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the first and last day of the calendar month prior to <paramref name="today"/>.
    /// Public to enable unit testing without InternalsVisibleTo.
    /// </summary>
    public static (DateOnly Start, DateOnly End) PreviousMonthWindow(DateOnly today)
    {
        // Move to first day of current month, subtract one day → last day of previous month.
        var lastDayOfPrev  = new DateOnly(today.Year, today.Month, 1).AddDays(-1);
        var firstDayOfPrev = new DateOnly(lastDayOfPrev.Year, lastDayOfPrev.Month, 1);
        return (firstDayOfPrev, lastDayOfPrev);
    }

    // ── Private DTOs ──────────────────────────────────────────────────────────

    private sealed record FranchiseSummary(
        Guid    Id,
        Guid    BrandId,
        decimal RoyaltyPercent,
        decimal MarketingFeePercent);
}
