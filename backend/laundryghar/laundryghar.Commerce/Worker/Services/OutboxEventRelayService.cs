using laundryghar.SharedDataModel.Persistence;
using laundryghar.Worker.Abstractions;
using laundryghar.Worker.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace laundryghar.Worker.Services;

/// <summary>
/// Background service that drains <c>kernel.outbox_events</c>.
/// Picks up rows in status 'pending' whose next_attempt_at is due, publishes them via
/// <see cref="IEventPublisher"/>, marks successful rows as 'published', and applies
/// exponential backoff on failure (moving to 'dead_letter' after max attempts).
/// </summary>
public sealed class OutboxEventRelayService : BackgroundService
{
    private readonly IServiceScopeFactory       _scopeFactory;
    private readonly ILogger<OutboxEventRelayService> _logger;
    private readonly WorkerOptions              _options;

    public OutboxEventRelayService(
        IServiceScopeFactory              scopeFactory,
        ILogger<OutboxEventRelayService>  logger,
        IOptions<WorkerOptions>           options)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
        _options      = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "OutboxEventRelayService starting (pollInterval={Interval}s, batchSize={Batch}).",
            _options.EventRelayPollIntervalSeconds,
            _options.EventBatchSize);

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
                // Transient error — log and continue; do NOT crash the host.
                _logger.LogError(ex, "OutboxEventRelayService: unhandled error in poll cycle; will retry next tick.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.EventRelayPollIntervalSeconds),
                stoppingToken);
        }

        _logger.LogInformation("OutboxEventRelayService stopped.");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope     = _scopeFactory.CreateAsyncScope();
        var db        = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        var now = DateTimeOffset.UtcNow;

        // Fetch pending events that are due (next_attempt_at <= now or not set yet).
        // Also pick up 'failed' rows that have been scheduled for retry.
        var batch = await db.OutboxEvents
            .Where(e =>
                (e.Status == "pending" || e.Status == "failed")
                && (e.NextAttemptAt == null || e.NextAttemptAt <= now)
                && e.PublishAttempts < _options.EventMaxAttempts)
            .OrderBy(e => e.OccurredAt)
            .Take(_options.EventBatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0)
            return;

        _logger.LogDebug("OutboxEventRelayService: processing {Count} outbox event(s).", batch.Count);

        foreach (var evtRow in batch)
        {
            await ProcessEventAsync(db, publisher, evtRow, ct);
        }
    }

    private async Task ProcessEventAsync(
        LaundryGharDbContext db,
        IEventPublisher      publisher,
        laundryghar.SharedDataModel.Entities.Kernel.OutboxEvent evtRow,
        CancellationToken ct)
    {
        // Mark as 'publishing' to prevent concurrent workers from picking it up.
        var lockStrategy = db.Database.CreateExecutionStrategy();
        await lockStrategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var current = await db.OutboxEvents
                .Where(e => e.Id == evtRow.Id && (e.Status == "pending" || e.Status == "failed"))
                .FirstOrDefaultAsync(ct);

            if (current is null)
            {
                await tx.RollbackAsync(ct);
                return;
            }

            current.Status = "publishing";
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        var publishRequest = new EventPublishRequest(
            EventId:        evtRow.Id,
            EventType:      evtRow.EventType,
            AggregateType:  evtRow.AggregateType,
            AggregateId:    evtRow.AggregateId,
            Payload:        evtRow.Payload,
            RoutingKey:     evtRow.RoutingKey,
            TargetExchange: evtRow.TargetExchange);

        bool succeeded  = false;
        string? errorMessage = null;

        try
        {
            await publisher.PublishAsync(publishRequest, ct);
            succeeded = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errorMessage = ex.Message;
            _logger.LogWarning(ex,
                "OutboxEventRelayService: publish failed for event {EventId} (type={EventType}).",
                evtRow.Id, evtRow.EventType);
        }

        // Persist outcome.
        var outcomeStrategy = db.Database.CreateExecutionStrategy();
        await outcomeStrategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = DateTimeOffset.UtcNow;

            var toUpdate = await db.OutboxEvents
                .Where(e => e.Id == evtRow.Id)
                .FirstOrDefaultAsync(ct);

            if (toUpdate is null)
            {
                await tx.RollbackAsync(ct);
                return;
            }

            toUpdate.PublishAttempts++;

            if (succeeded)
            {
                toUpdate.Status      = "published";
                toUpdate.PublishedAt = now;
                toUpdate.LastError   = null;

                _logger.LogInformation(
                    "OutboxEventRelayService: published event {EventId} (type={EventType} aggregate={AggType}:{AggId}).",
                    toUpdate.Id, toUpdate.EventType, toUpdate.AggregateType, toUpdate.AggregateId);
            }
            else
            {
                // Exponential backoff: 2^attempts minutes, capped at 24 h.
                var backoffMinutes = Math.Min(Math.Pow(2, toUpdate.PublishAttempts), 1440);
                toUpdate.NextAttemptAt = now.AddMinutes(backoffMinutes);
                toUpdate.LastError     = errorMessage;

                if (toUpdate.PublishAttempts >= _options.EventMaxAttempts)
                {
                    toUpdate.Status = "dead_letter";
                    _logger.LogError(
                        "OutboxEventRelayService: event {EventId} moved to dead_letter after {Attempts} attempts.",
                        toUpdate.Id, toUpdate.PublishAttempts);
                }
                else
                {
                    toUpdate.Status = "failed";
                    _logger.LogDebug(
                        "OutboxEventRelayService: event {EventId} will retry at {Next}.",
                        toUpdate.Id, toUpdate.NextAttemptAt);
                }
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }
}
