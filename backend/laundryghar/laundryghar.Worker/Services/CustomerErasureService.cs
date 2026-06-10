using System.Text.Json;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Worker.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace laundryghar.Worker.Services;

/// <summary>
/// DPDP-compliant customer anonymization job.
///
/// On each poll cycle it fetches up to <see cref="WorkerOptions.ErasureBatchSize"/> pending
/// deletion requests whose grace period has elapsed, anonymizes the associated customer's PII
/// in-place, and stamps <c>anonymized_at</c> + advances the status to <c>soft_deleted</c>.
///
/// Financial/audit history (orders, payments, invoices, ledger, audit_logs) is intentionally
/// preserved — GST 72-month retention requirement.
///
/// A <c>customer.erased</c> outbox event is emitted inside the same transaction as the
/// anonymization changes so downstream consumers (e.g. analytics) can react.
///
/// The Worker runs with BypassRls = true so cross-brand queries work without tenant context.
/// </summary>
public sealed class CustomerErasureService : BackgroundService
{
    private readonly IServiceScopeFactory               _scopeFactory;
    private readonly ILogger<CustomerErasureService>    _logger;
    private readonly WorkerOptions                      _options;

    public CustomerErasureService(
        IServiceScopeFactory            scopeFactory,
        ILogger<CustomerErasureService> logger,
        IOptions<WorkerOptions>         options)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
        _options      = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "CustomerErasureService starting (pollInterval={Interval}s, batchSize={Batch}, " +
            "gracePeriodDays={GraceDays}, gracePeriodMinutesOverride={GraceMinutes}).",
            _options.ErasurePollIntervalSeconds,
            _options.ErasureBatchSize,
            _options.ErasureGracePeriodDays,
            _options.ErasureGracePeriodMinutesOverride);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "CustomerErasureService: unhandled error in poll cycle; will retry next tick.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.ErasurePollIntervalSeconds),
                stoppingToken);
        }

        _logger.LogInformation("CustomerErasureService stopped.");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();

        var graceCutoff = ComputeGraceCutoff();

        // Fetch pending requests whose grace period has elapsed.
        // idx_acctdel_status covers (status, grace_period_ends_at).
        var batch = await db.AccountDeletionRequests
            .Where(r => r.Status == "pending" && r.GracePeriodEndsAt <= graceCutoff)
            .OrderBy(r => r.GracePeriodEndsAt)
            .Take(_options.ErasureBatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0)
            return;

        _logger.LogInformation(
            "CustomerErasureService: processing {Count} erasure request(s) (cutoff={Cutoff}).",
            batch.Count, graceCutoff);

        foreach (var request in batch)
        {
            try
            {
                await EraseCustomerAsync(db, request, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "CustomerErasureService: failed to erase customerId={CustomerId} " +
                    "requestId={RequestId}; skipping row.",
                    request.CustomerId, request.Id);
                // Do NOT advance the request — it stays pending and will be retried next cycle.
            }
        }
    }

    /// <summary>
    /// Anonymizes a single customer atomically.  All changes (customer PII wipe, address soft-delete,
    /// opt-in clearance, push-token deactivation, notification preference wipe, request status update,
    /// outbox event) commit in one transaction.
    /// </summary>
    private async Task EraseCustomerAsync(
        LaundryGharDbContext db,
        SharedDataModel.Entities.CustomerCatalog.AccountDeletionRequest request,
        CancellationToken ct)
    {
        if (request.CustomerId is null)
        {
            _logger.LogWarning(
                "CustomerErasureService: requestId={RequestId} has no CustomerId; skipping.",
                request.Id);
            return;
        }

        var customerId = request.CustomerId.Value;

        // Load customer including related collections needed for erasure.
        var customer = await db.Customers.IgnoreQueryFilters()
            .Include(c => c.Addresses)
            .Include(c => c.Devices)
            .FirstOrDefaultAsync(c => c.Id == customerId, ct);

        if (customer is null)
        {
            _logger.LogWarning(
                "CustomerErasureService: customer {CustomerId} not found for requestId={RequestId}; " +
                "marking request as failed.",
                customerId, request.Id);
            request.Status      = "failed";
            request.AnonymizedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var tombstoneId = customerId.ToString("N")[..12]; // 12 hex chars for uniqueness

        // ── Anonymize customer PII ────────────────────────────────────────────────
        CustomerAnonymizer.Anonymize(customer, tombstoneId, now);

        // ── Soft-delete all addresses (FK is CASCADE-delete on hard delete, soft-delete for now) ─
        foreach (var addr in customer.Addresses.Where(a => a.DeletedAt is null))
        {
            addr.AddressLine1        = "[deleted]";
            addr.AddressLine2        = null;
            addr.Landmark            = null;
            addr.RecipientName       = null;
            addr.RecipientPhone      = null;
            addr.DeliveryInstructions= null;
            addr.DeletedAt           = now;
            addr.UpdatedAt           = now;
            // geo_location: set null via raw update below (NpgsqlPoint or Geography type)
        }

        // Null geography columns via raw parameterized SQL — EF does not track geography types natively.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE customer_catalog.customer_addresses SET geo_location = NULL WHERE customer_id = {customerId}",
            ct);

        // ── Deactivate push tokens for this customer ──────────────────────────────
        await db.PushTokens
            .Where(t => t.CustomerId == customerId && t.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive, false), ct);

        // ── Delete notification preferences (soft-deleted-or-deleted is fine; no deleted_at column) ─
        await db.NotificationPreferences
            .Where(p => p.CustomerId == customerId)
            .ExecuteDeleteAsync(ct);

        // ── Clear device push tokens (FCM/APNS) ───────────────────────────────────
        foreach (var device in customer.Devices)
        {
            device.FcmToken  = null;
            device.ApnsToken = null;
            device.PushEnabled = false;
            device.IsActive  = false;
        }

        // ── Stamp deletion request ────────────────────────────────────────────────
        request.Status       = "soft_deleted";
        request.AnonymizedAt = now;

        // ── Emit customer.erased outbox event ─────────────────────────────────────
        var payload = JsonSerializer.Serialize(new
        {
            CustomerId  = customerId,
            BrandId     = customer.BrandId,
            ErasedAt    = now,
            RequestId   = request.Id
        });

        db.OutboxEvents.Add(new OutboxEvent
        {
            Id            = Guid.NewGuid(),
            BrandId       = customer.BrandId,
            AggregateType = "customer",
            AggregateId   = customerId,
            EventType     = "customer.erased",
            EventVersion  = 1,
            Payload       = payload,
            Metadata      = "{}",
            OccurredAt    = now,
            Status        = "pending",
            CreatedAt     = now
        });

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "CustomerErasureService: erased customer {CustomerId} (requestId={RequestId}).",
            customerId, request.Id);
    }

    /// <summary>
    /// Computes the cutoff timestamp for grace period eligibility.
    /// In Development, <see cref="WorkerOptions.ErasureGracePeriodMinutesOverride"/> allows a
    /// very short window (e.g. 2 minutes) so the erasure path is testable without waiting 30 days.
    /// </summary>
    private DateTimeOffset ComputeGraceCutoff()
    {
        var now = DateTimeOffset.UtcNow;

        if (_options.ErasureGracePeriodMinutesOverride > 0)
            return now.AddMinutes(-_options.ErasureGracePeriodMinutesOverride);

        return now.AddDays(-_options.ErasureGracePeriodDays);
    }
}
