namespace laundryghar.Orders.Application.Orders.Dtos;

// ── Ops queue DTOs ───────────────────────────────────────────────────────────

/// <summary>
/// Lean order summary used in the ops queues (due today / overdue / stuck).
/// Keeps the payload small for high-frequency polling.
/// </summary>
public sealed record OpsOrderDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    string OrderNumber,
    string CustomerName,
    string Status,
    DateTimeOffset? PromisedDeliveryAt,
    /// <summary>Hours the order is overdue (positive if past promised_delivery_at). Null when not overdue.</summary>
    double? HoursOverdue,
    /// <summary>Hours since the last status_history entry. Populated for the "stuck" queue.</summary>
    double? HoursStuck
);

/// <summary>Summary counts for a single ops queue bucket.</summary>
public sealed record OpsQueueBucketSummary(int Count);

/// <summary>
/// Response shape for GET /api/v1/admin/orders/ops-queues.
/// Each bucket carries a count (for badge display) and a paged list of orders.
/// </summary>
public sealed record OpsQueuesResponse(
    OpsQueueBucket DueToday,
    OpsQueueBucket Overdue,
    OpsQueueBucket Stuck
);

public sealed record OpsQueueBucket(
    int Count,
    IReadOnlyList<OpsOrderDto> List,
    bool HasNextPage,
    int TotalCount
);
