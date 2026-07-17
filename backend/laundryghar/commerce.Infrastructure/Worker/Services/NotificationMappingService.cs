using System.Text.Json;
using commerce.Infrastructure.Worker.Channels;
using commerce.Infrastructure.Worker.Options;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace commerce.Infrastructure.Worker.Services;

/// <summary>
/// Background service that consumes <c>kernel.outbox_events</c> for order/payment
/// lifecycle events and enqueues corresponding <c>engagement_cms.notifications_outbox</c>
/// rows so <see cref="NotificationDispatcherService"/> can dispatch them.
///
/// Deduplication: maintains its own watermark in
/// <c>engagement_cms.notification_event_cursors</c> (consumer_name = 'notification_mapper').
/// This is independent of <see cref="OutboxEventRelayService"/> so the two services
/// do not compete for the same rows.
///
/// Channel selection: delegates to <see cref="NotificationChannelPreferencePolicy"/>
/// using the customer's opt-in flags. Suppressed notifications are logged at Debug
/// with a suppression_reason in the outbox row.
///
/// Template resolution: looks up engagement_cms.notification_templates by
/// (brand_id, code, channel, locale). Falls back gracefully if no template is found —
/// the body is empty but the outbox row is still created so the dispatcher can log it.
/// </summary>
public sealed class NotificationMappingService : BackgroundService
{
    private const string ConsumerName = "notification_mapper";

    private readonly IServiceScopeFactory      _scopeFactory;
    private readonly ILogger<NotificationMappingService> _logger;
    private readonly WorkerOptions             _options;

    public NotificationMappingService(
        IServiceScopeFactory              scopeFactory,
        ILogger<NotificationMappingService> logger,
        IOptions<WorkerOptions>           options)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
        _options      = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "NotificationMappingService starting (pollInterval={Interval}s, batchSize={Batch}).",
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
                _logger.LogError(ex,
                    "NotificationMappingService: unhandled error in poll cycle; will retry next tick.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.EventRelayPollIntervalSeconds),
                stoppingToken);
        }

        _logger.LogInformation("NotificationMappingService stopped.");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateWorkerAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();

        // Load cursor (watermark).
        var cursor = await db.NotificationEventCursors
            .Where(c => c.ConsumerName == ConsumerName)
            .FirstOrDefaultAsync(ct);

        if (cursor is null)
        {
            // Should not happen post-patch, but be defensive.
            cursor = new NotificationEventCursor
            {
                ConsumerName   = ConsumerName,
                LastEventId    = null,
                ProcessedCount = 0,
                UpdatedAt      = DateTimeOffset.UtcNow
            };
            db.NotificationEventCursors.Add(cursor);
            await db.SaveChangesAsync(ct);
        }

        // Fetch the next batch of relevant events AFTER the watermark.
        // We use OccurredAt ordering combined with an Id tiebreaker.
        // If LastEventId is null, start from the beginning of time.
        var relevantTypes = new[]
        {
            "order.status_changed",
            "order.cancelled",
            "payment.captured",
            "refund.initiated",
            "fulfillment.lost",
            "pickup.rejected"
        };

        IQueryable<laundryghar.SharedDataModel.Entities.Kernel.OutboxEvent> query = db.OutboxEvents
            .Where(e => relevantTypes.Contains(e.EventType));

        if (cursor.LastEventId.HasValue)
        {
            // Get the occurred_at of the last processed event for range filter.
            var lastEvent = await db.OutboxEvents
                .Where(e => e.Id == cursor.LastEventId.Value)
                .Select(e => new { e.OccurredAt, e.Id })
                .FirstOrDefaultAsync(ct);

            if (lastEvent is not null)
            {
                // Fetch events strictly after the cursor position.
                // Use (occurred_at, id) tuple ordering to avoid gaps.
                query = query.Where(e =>
                    e.OccurredAt > lastEvent.OccurredAt
                    || (e.OccurredAt == lastEvent.OccurredAt && e.Id > lastEvent.Id));
            }
        }

        var batch = await query
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .Take(_options.EventBatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0)
            return;

        _logger.LogDebug(
            "NotificationMappingService: mapping {Count} event(s) after cursor={LastId}.",
            batch.Count, cursor.LastEventId);

        int enqueued = 0;
        Guid lastProcessedId = cursor.LastEventId ?? Guid.Empty;

        foreach (var evt in batch)
        {
            try
            {
                var count = await MapEventToOutboxAsync(db, evt, ct);
                enqueued += count;
                lastProcessedId = evt.Id;
            }
            catch (Exception ex)
            {
                // Log and continue — one bad event must not stall the entire batch.
                _logger.LogError(ex,
                    "NotificationMappingService: failed to map event {EventId} (type={EventType}); skipping.",
                    evt.Id, evt.EventType);
                lastProcessedId = evt.Id; // Advance past the bad event.
            }
        }

        // Advance watermark.
        cursor.LastEventId    = lastProcessedId;
        cursor.ProcessedCount += batch.Count;
        cursor.UpdatedAt      = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        if (enqueued > 0)
            _logger.LogInformation(
                "NotificationMappingService: enqueued {Enqueued} notification(s) from {Batch} event(s).",
                enqueued, batch.Count);
    }

    /// <summary>
    /// Maps a single outbox event to zero or more notifications_outbox rows.
    /// Returns the count of rows actually inserted.
    /// </summary>
    private async Task<int> MapEventToOutboxAsync(
        LaundryGharDbContext db,
        laundryghar.SharedDataModel.Entities.Kernel.OutboxEvent evt,
        CancellationToken ct)
    {
        // Parse the payload.
        EventPayload? payload = null;
        try
        {
            payload = JsonSerializer.Deserialize<EventPayload>(evt.Payload,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "NotificationMappingService: could not parse payload for event {EventId}.", evt.Id);
        }

        var orderId    = payload?.OrderId;
        // Orders service emits "toStatus" (camelCase); NewStatus is a legacy/alias field.
        var newStatus  = payload?.NewStatus ?? payload?.ToStatus ?? payload?.Status;
        var brandId    = evt.BrandId;

        // Resolve template info.
        var templateInfo = NotificationChannelPreferencePolicy.ResolveTemplate(evt.EventType, newStatus);
        if (templateInfo is null)
        {
            _logger.LogDebug(
                "NotificationMappingService: no template mapping for event {EventType} status={Status}; skipping.",
                evt.EventType, newStatus);
            return 0;
        }

        if (!brandId.HasValue)
        {
            _logger.LogDebug(
                "NotificationMappingService: event {EventId} has no brand_id; skipping.", evt.Id);
            return 0;
        }

        // Resolve the customer for this event.
        var customer = await ResolveCustomerAsync(db, evt, payload, brandId.Value, ct);
        if (customer is null)
        {
            _logger.LogDebug(
                "NotificationMappingService: could not resolve customer for event {EventId}; skipping.",
                evt.Id);
            return 0;
        }

        // Apply channel-preference ladder.
        var channel = NotificationChannelPreferencePolicy.ResolveChannel(
            customer.WhatsappOptIn,
            customer.SmsOptIn,
            customer.PushOptIn);

        if (channel is null)
        {
            _logger.LogDebug(
                "NotificationMappingService: customer {CustomerId} has opted out of all channels; " +
                "suppressing event {EventId}.",
                customer.Id, evt.Id);
            return 0;
        }

        var templateCode = NotificationChannelPreferencePolicy.BuildTemplateCode(
            templateInfo.Value.TemplateCode, channel);

        // Resolve template body.
        var template = await db.NotificationTemplates
            .Where(t => t.BrandId == brandId.Value
                     && t.Code    == templateCode
                     && t.Status  == "active")
            .FirstOrDefaultAsync(ct);

        // Build the variable map for rendering.
        var variables = BuildVariables(customer, payload, evt);
        var variablesJson = JsonSerializer.Serialize(variables);

        // Render the body inline (fallback to a sensible default if template missing).
        string body;
        if (template is not null)
        {
            body = RenderTemplate(template.BodyTemplate, variables);
        }
        else
        {
            _logger.LogWarning(
                "NotificationMappingService: template code={Code} for brand={BrandId} not found; " +
                "using fallback body.",
                templateCode, brandId);
            body = BuildFallbackBody(evt.EventType, newStatus, payload);
        }

        // Idempotency key: event_id + channel prevents duplicate enqueue on restarts.
        var idempotencyKey = $"evt:{evt.Id}:ch:{channel}";

        // Check dedup by idempotency key.
        var alreadyExists = await db.NotificationOutboxes
            .AnyAsync(n => n.IdempotencyKey == idempotencyKey, ct);

        if (alreadyExists)
        {
            _logger.LogDebug(
                "NotificationMappingService: outbox row already exists for idempotencyKey={Key}; skipping.",
                idempotencyKey);
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        var outbox = new NotificationOutbox
        {
            Id              = Guid.NewGuid(),
            BrandId         = brandId.Value,
            TemplateId      = template?.Id,
            TemplateCode    = templateCode,
            Channel         = channel,
            Locale          = customer.Locale ?? "en",
            RecipientType   = "customer",
            RecipientId     = customer.Id,
            RecipientPhone  = customer.PhoneE164,
            RecipientEmail  = customer.Email,
            Body            = body,
            VariablesResolved = variablesJson,
            ReferenceType   = "order",
            ReferenceId     = orderId ?? evt.AggregateId,
            CorrelationId   = evt.CorrelationId,
            Priority        = 1,
            ScheduledAt     = now,
            Attempts        = 0,
            MaxAttempts     = 5,
            Status          = "pending",
            IdempotencyKey  = idempotencyKey,
            CreatedAt       = now
        };

        // Not saved here — the caller flushes the whole batch (plus the cursor advance) in one
        // round trip. Safe because idempotencyKey is derived from evt.Id, which is unique within
        // a single query result, so there is no risk of two rows in the same unsaved batch racing
        // the AnyAsync dedup check above.
        db.NotificationOutboxes.Add(outbox);

        _logger.LogDebug(
            "NotificationMappingService: queued outbox row {OutboxId} channel={Channel} " +
            "template={Template} for customer {CustomerId}.",
            outbox.Id, channel, templateCode, customer.Id);

        return 1;
    }

    private async Task<Customer?> ResolveCustomerAsync(
        LaundryGharDbContext db,
        laundryghar.SharedDataModel.Entities.Kernel.OutboxEvent evt,
        EventPayload? payload,
        Guid brandId,
        CancellationToken ct)
    {
        // Primary: payload CustomerId.
        if (payload?.CustomerId.HasValue == true)
        {
            return await db.Customers.IgnoreQueryFilters()
                .Where(c => c.Id == payload.CustomerId.Value && c.BrandId == brandId)
                .FirstOrDefaultAsync(ct);
        }

        // Secondary: look up via the order aggregate.
        if (evt.AggregateType.Equals("order", StringComparison.OrdinalIgnoreCase))
        {
            return await db.Customers.IgnoreQueryFilters()
                .Where(c => c.BrandId == brandId
                    && db.Orders.Any(o => o.Id == evt.AggregateId && o.CustomerId == c.Id))
                .FirstOrDefaultAsync(ct);
        }

        // Tertiary: look up via the fulfillment aggregate (e.g. fulfillment.lost events).
        if (evt.AggregateType.Equals("fulfillment", StringComparison.OrdinalIgnoreCase))
        {
            return await db.Customers.IgnoreQueryFilters()
                .Where(c => c.BrandId == brandId
                    && db.FulfillmentUnits.Any(g => g.Id == evt.AggregateId && g.CustomerId == c.Id))
                .FirstOrDefaultAsync(ct);
        }

        // Quaternary: look up via the pickup_request aggregate (e.g. pickup.rejected events).
        if (evt.AggregateType.Equals("pickup_request", StringComparison.OrdinalIgnoreCase))
        {
            return await db.Customers.IgnoreQueryFilters()
                .Where(c => c.BrandId == brandId
                    && db.PickupRequests.Any(p => p.Id == evt.AggregateId && p.CustomerId == c.Id))
                .FirstOrDefaultAsync(ct);
        }

        return null;
    }

    private static Dictionary<string, string> BuildVariables(
        Customer customer,
        EventPayload? payload,
        laundryghar.SharedDataModel.Entities.Kernel.OutboxEvent evt)
    {
        var name = customer.DisplayName
            ?? (customer.FirstName is not null
                ? $"{customer.FirstName} {customer.LastName}".Trim()
                : customer.PhoneE164);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["customer_name"]    = name,
            ["order_number"]     = payload?.OrderNumber ?? evt.AggregateId.ToString()[..8],
            ["order_status"]     = payload?.NewStatus ?? payload?.ToStatus ?? payload?.Status ?? "",
            ["amount"]           = payload?.Amount?.ToString("F2") ?? "",
            ["tracking_url"]     = payload?.TrackingUrl ?? "",
            ["pickup_date"]      = payload?.PickupDate ?? "",
            ["delivery_date"]    = payload?.DeliveryDate ?? "",
            // Pickup-specific variables (present on pickup.rejected events).
            ["request_number"]   = payload?.RequestNumber ?? "",
            ["rejection_reason"] = payload?.Reason ?? ""
        };
    }

    private static string RenderTemplate(
        string template,
        Dictionary<string, string> variables)
    {
        foreach (var (key, value) in variables)
            template = template.Replace($"{{{{{key}}}}}", value, StringComparison.OrdinalIgnoreCase);
        return template;
    }

    private static string BuildFallbackBody(
        string eventType,
        string? newStatus,
        EventPayload? payload)
    {
        return eventType switch
        {
            "order.status_changed" => $"Your Laundry Ghar order {payload?.OrderNumber ?? ""} status has been updated to {newStatus ?? ""}.",
            "order.cancelled"      => $"Your Laundry Ghar order {payload?.OrderNumber ?? ""} has been cancelled.",
            "payment.captured"     => $"Payment of ₹{payload?.Amount?.ToString("F2") ?? ""} received for your Laundry Ghar order.",
            "refund.initiated"     => $"A refund of ₹{payload?.Amount?.ToString("F2") ?? ""} has been initiated for your Laundry Ghar order.",
            "fulfillment.lost"         => $"We're sorry — one of your garments from order {payload?.OrderNumber ?? ""} could not be located during our warehouse reconciliation. Our team will contact you shortly.",
            "pickup.rejected"      => $"Your Laundry Ghar pickup request {payload?.RequestNumber ?? ""} could not be fulfilled. Reason: {payload?.Reason ?? ""}. Please book a new slot.",
            _                      => "Update from Laundry Ghar."
        };
    }

    /// <summary>
    /// Minimal DTO used to deserialize only the fields we care about from the event payload JSON.
    /// Unknown fields are silently ignored.
    /// </summary>
    private sealed class EventPayload
    {
        public Guid?    OrderId         { get; set; }
        public string?  OrderNumber     { get; set; }
        public Guid?    CustomerId      { get; set; }
        public string?  NewStatus       { get; set; }
        /// <summary>Orders service emits "toStatus" (camelCase) for status-changed events.</summary>
        public string?  ToStatus        { get; set; }
        public string?  Status          { get; set; }
        public decimal? Amount          { get; set; }
        public string?  TrackingUrl     { get; set; }
        public string?  PickupDate      { get; set; }
        public string?  DeliveryDate    { get; set; }
        /// <summary>Present on pickup.rejected events.</summary>
        public string?  RequestNumber   { get; set; }
        /// <summary>Present on pickup.rejected events — the admin-supplied rejection reason.</summary>
        public string?  Reason          { get; set; }
    }
}
