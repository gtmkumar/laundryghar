using laundryghar.SharedDataModel.Entities.EngagementCms;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Worker.Abstractions;
using laundryghar.Worker.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace laundryghar.Worker.Services;

/// <summary>
/// Background service that drains <c>engagement_cms.notifications_outbox</c>.
/// Each poll cycle picks up a small batch of rows in status 'pending' or 'failed'
/// whose next_attempt_at is due, dispatches them via <see cref="IChannelSender"/>,
/// writes a <c>notifications_log</c> audit row on success, and applies exponential
/// backoff on failure. One DB transaction per row guards against partial batches.
/// </summary>
public sealed class NotificationDispatcherService : BackgroundService
{
    private readonly IServiceScopeFactory          _scopeFactory;
    private readonly ILogger<NotificationDispatcherService> _logger;
    private readonly WorkerOptions                 _options;

    public NotificationDispatcherService(
        IServiceScopeFactory                 scopeFactory,
        ILogger<NotificationDispatcherService> logger,
        IOptions<WorkerOptions>              options)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
        _options      = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "NotificationDispatcherService starting (pollInterval={Interval}s, batchSize={Batch}).",
            _options.NotificationPollIntervalSeconds,
            _options.NotificationBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown — let the while loop exit.
                break;
            }
            catch (Exception ex)
            {
                // Transient DB or sender error — log and continue; do NOT crash the host.
                _logger.LogError(ex, "NotificationDispatcherService: unhandled error in poll cycle; will retry next tick.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.NotificationPollIntervalSeconds),
                stoppingToken);
        }

        _logger.LogInformation("NotificationDispatcherService stopped.");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        // Each poll cycle gets its own DI scope so the scoped DbContext + interceptor
        // are fresh (important: the interceptor sets RLS vars per-scope).
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db     = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IChannelSender>();

        var now = DateTimeOffset.UtcNow;

        // Fetch a batch: pending or failed rows that are due, ordered by priority
        // (lower value = higher priority) then by creation time (oldest first).
        var batch = await db.NotificationOutboxes
            .Where(n =>
                (n.Status == "pending" || n.Status == "failed")
                && (n.NextAttemptAt == null || n.NextAttemptAt <= now)
                && n.Attempts < n.MaxAttempts)
            .OrderBy(n => n.Priority)
            .ThenBy(n => n.CreatedAt)
            .Take(_options.NotificationBatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0)
            return;

        _logger.LogDebug("NotificationDispatcherService: processing {Count} outbox row(s).", batch.Count);

        foreach (var row in batch)
        {
            await ProcessRowAsync(db, sender, row, ct);
        }
    }

    private async Task ProcessRowAsync(
        LaundryGharDbContext db,
        IChannelSender       sender,
        NotificationOutbox   row,
        CancellationToken    ct)
    {
        // Use Npgsql execution strategy so the transaction participates in the retry policy.
        // Direct BeginTransactionAsync() throws when EnableRetryOnFailure is configured.
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Re-read the row inside the transaction to guard against concurrent workers
            // picking up the same row. Skip if another worker already changed the status.
            var current = await db.NotificationOutboxes
                .Where(n => n.Id == row.Id && (n.Status == "pending" || n.Status == "failed"))
                .FirstOrDefaultAsync(ct);

            if (current is null)
            {
                await tx.RollbackAsync(ct);
                return;
            }

            // Mark as sending to prevent concurrent re-pick.
            current.Status        = "sending";
            current.LastAttemptAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        });

        // Dispatch outside the lock transaction — real I/O should not hold the DB lock.
        // Resolve the template body for rendering if a template_id is present.
        string renderedBody = row.Body;
        NotificationTemplate? template = null;

        if (row.TemplateId.HasValue)
        {
            template = await db.NotificationTemplates
                .Where(t => t.Id == row.TemplateId.Value)
                .FirstOrDefaultAsync(ct);

            if (template is not null && row.VariablesResolved is not null)
            {
                renderedBody = RenderTemplate(template.BodyTemplate, row.VariablesResolved);
            }
        }

        var sendRequest = new ChannelSendRequest(
            OutboxId:      row.Id,
            BrandId:       row.BrandId,
            Channel:       row.Channel,
            RecipientType: row.RecipientType,
            RecipientId:   row.RecipientId,
            RecipientPhone: row.RecipientPhone,
            RecipientEmail: row.RecipientEmail,
            TemplateCode:  row.TemplateCode,
            Body:          renderedBody,
            ReferenceType: row.ReferenceType,
            ReferenceId:   row.ReferenceId);

        bool succeeded = false;
        string? errorMessage = null;
        string providerName = "unknown";
        string? providerMessageId = null;

        try
        {
            var result = await sender.SendAsync(sendRequest, ct);
            succeeded         = true;
            providerName      = result.ProviderName;
            providerMessageId = result.ProviderMessageId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errorMessage = ex.Message;
            _logger.LogWarning(ex,
                "NotificationDispatcherService: send failed for outbox row {OutboxId} (channel={Channel}).",
                row.Id, row.Channel);
        }

        // Persist the outcome in a second transaction.
        var outcomeStrategy = db.Database.CreateExecutionStrategy();
        await outcomeStrategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var now = DateTimeOffset.UtcNow;
            var toUpdate = await db.NotificationOutboxes
                .Where(n => n.Id == row.Id)
                .FirstOrDefaultAsync(ct);

            if (toUpdate is null)
            {
                await tx.RollbackAsync(ct);
                return;
            }

            toUpdate.Attempts++;

            if (succeeded)
            {
                toUpdate.Status            = "sent";
                toUpdate.SentAt            = now;
                toUpdate.Provider          = providerName;
                toUpdate.ProviderMessageId = providerMessageId;
                toUpdate.LastError         = null;

                // Append audit log row (composite PK: Id + SentAt).
                var log = new NotificationLog
                {
                    Id               = Guid.NewGuid(),
                    SentAt           = now,
                    BrandId          = row.BrandId,
                    OutboxId         = row.Id,
                    Channel          = row.Channel,
                    TemplateCode     = row.TemplateCode,
                    RecipientType    = row.RecipientType,
                    RecipientId      = row.RecipientId,
                    RecipientAddress = row.RecipientPhone ?? row.RecipientEmail,
                    Provider         = providerName,
                    Status           = "sent",
                    ReferenceType    = row.ReferenceType,
                    ReferenceId      = row.ReferenceId,
                    CreatedAt        = now
                };
                db.NotificationLogs.Add(log);

                _logger.LogInformation(
                    "NotificationDispatcherService: dispatched outbox row {OutboxId} (channel={Channel}).",
                    row.Id, row.Channel);
            }
            else
            {
                // Back-off: 2^attempts minutes; cap at 24 h.
                var backoffMinutes  = Math.Min(Math.Pow(2, toUpdate.Attempts), 1440);
                toUpdate.NextAttemptAt = now.AddMinutes(backoffMinutes);
                toUpdate.LastError     = errorMessage;

                if (toUpdate.Attempts >= toUpdate.MaxAttempts)
                {
                    toUpdate.Status = "failed";
                    _logger.LogWarning(
                        "NotificationDispatcherService: outbox row {OutboxId} exhausted attempts ({Max}); status=failed.",
                        row.Id, toUpdate.MaxAttempts);
                }
                else
                {
                    toUpdate.Status = "pending";
                    _logger.LogDebug(
                        "NotificationDispatcherService: outbox row {OutboxId} will retry at {Next}.",
                        row.Id, toUpdate.NextAttemptAt);
                }
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    /// <summary>Simple {{variable}} token substitution using the stored JSON variable map.</summary>
    private static string RenderTemplate(string template, string variablesJson)
    {
        try
        {
            var variables = System.Text.Json.JsonSerializer
                .Deserialize<Dictionary<string, string>>(variablesJson);

            if (variables is null) return template;

            foreach (var (key, value) in variables)
                template = template.Replace($"{{{{{key}}}}}", value, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Defensive: if the JSON is malformed, return the raw template body.
        }

        return template;
    }
}
