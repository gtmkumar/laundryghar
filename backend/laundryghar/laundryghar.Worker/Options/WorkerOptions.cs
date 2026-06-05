namespace laundryghar.Worker.Options;

/// <summary>Configuration options for the notification dispatcher and event relay loops.</summary>
public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>How often the notification dispatcher polls for pending outbox rows (seconds).</summary>
    public int NotificationPollIntervalSeconds { get; set; } = 5;

    /// <summary>How often the event relay polls for pending outbox_events rows (seconds).</summary>
    public int EventRelayPollIntervalSeconds { get; set; } = 5;

    /// <summary>Maximum rows processed per poll cycle by the notification dispatcher.</summary>
    public int NotificationBatchSize { get; set; } = 20;

    /// <summary>Maximum rows processed per poll cycle by the event relay.</summary>
    public int EventBatchSize { get; set; } = 20;

    /// <summary>Maximum publish attempts before an outbox_event is moved to dead_letter.</summary>
    public int EventMaxAttempts { get; set; } = 10;
}
