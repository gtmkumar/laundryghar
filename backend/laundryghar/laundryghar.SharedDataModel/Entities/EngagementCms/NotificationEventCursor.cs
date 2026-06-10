namespace laundryghar.SharedDataModel.Entities.EngagementCms;

/// <summary>
/// Per-consumer watermark for kernel.outbox_events consumption
/// (engagement_cms.notification_event_cursors).
/// Each named consumer tracks its own last-processed event id independently.
/// NotificationMappingService uses consumer_name = 'notification_mapper'.
/// </summary>
public class NotificationEventCursor
{
    /// <summary>Primary key — logical name of the consuming service (e.g. 'notification_mapper').</summary>
    public string ConsumerName { get; set; } = null!;

    /// <summary>Id of the last kernel.outbox_events row successfully mapped; null = not started yet.</summary>
    public Guid? LastEventId { get; set; }

    public long ProcessedCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
