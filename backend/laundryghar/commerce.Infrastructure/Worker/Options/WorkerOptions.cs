namespace commerce.Infrastructure.Worker.Options;

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

    // ── DPDP Erasure ─────────────────────────────────────────────────────────────

    /// <summary>
    /// How often the customer erasure job polls for requests whose grace period has elapsed (seconds).
    /// Default: 3600 (1 hour). Override to a short value in Development for fast testing.
    /// </summary>
    public int ErasurePollIntervalSeconds { get; set; } = 3600;

    /// <summary>
    /// The DPDP grace period before a pending deletion request is eligible for anonymization (days).
    /// Default: 30. Override to a small fractional-day value (expressed as minutes) via
    /// <see cref="ErasureGracePeriodMinutesOverride"/> in Development.
    /// </summary>
    public int ErasureGracePeriodDays { get; set; } = 30;

    /// <summary>
    /// Development override: when &gt; 0, overrides <see cref="ErasureGracePeriodDays"/> and treats
    /// the grace period as this many minutes instead. Ignored in Production (set to 0 or omit).
    /// </summary>
    public int ErasureGracePeriodMinutesOverride { get; set; } = 0;

    /// <summary>Maximum deletion requests processed per erasure cycle.</summary>
    public int ErasureBatchSize { get; set; } = 10;

    // ── Retention Sweep ───────────────────────────────────────────────────────────

    /// <summary>How often the retention sweep runs (seconds). Default: 86400 (once/day).</summary>
    public int RetentionSweepIntervalSeconds { get; set; } = 86400;

    /// <summary>Delete notifications_outbox rows older than this many days. Default: 180.</summary>
    public int NotificationOutboxRetentionDays { get; set; } = 180;

    /// <summary>Delete expired otp_codes rows older than this many days past expiry. Default: 30.</summary>
    public int OtpCodeRetentionDays { get; set; } = 30;

    /// <summary>Delete revoked/expired refresh_tokens rows older than this many days. Default: 90.</summary>
    public int RefreshTokenRetentionDays { get; set; } = 90;

    // ── Royalty Generation ─────────────────────────────────────────────────────────

    /// <summary>
    /// Master switch for the monthly royalty auto-generation job.
    /// Default: <c>false</c> — must be explicitly enabled.
    /// Set <c>Worker:RoyaltyGenerationEnabled=true</c> in appsettings or env vars to activate.
    /// </summary>
    public bool RoyaltyGenerationEnabled { get; set; } = false;

    /// <summary>
    /// Day of month (1–28) on which the royalty generation job fires.
    /// Default: 1 (first of the month). Generates invoices for the PREVIOUS calendar month.
    /// </summary>
    public int RoyaltyGenerationDayOfMonth { get; set; } = 1;

    /// <summary>
    /// How often the royalty generation job polls to check whether today is the trigger day (seconds).
    /// Default: 86400 (once per day). In Development, set to a small value to fast-test
    /// without waiting for midnight, then pair with <see cref="RoyaltyGenerationDayOfMonth"/>
    /// matching today's day.
    /// </summary>
    public int RoyaltyGenerationPollIntervalSeconds { get; set; } = 86400;

    // ── Auto-Dispatch ──────────────────────────────────────────────────────────────

    // ── Daily Reconciliation ───────────────────────────────────────────────────────

    /// <summary>
    /// Master switch for the daily warehouse reconciliation auto-creation job.
    /// Default: <c>false</c> — must be explicitly enabled.
    /// Set <c>Worker:DailyReconEnabled=true</c> to activate.
    /// </summary>
    public bool DailyReconEnabled { get; set; } = false;

    /// <summary>
    /// Local hour (0–23, IST/UTC+5:30) at which the daily recon job creates sessions.
    /// Default: 21 (9 PM IST — after the last shift typically ends).
    /// </summary>
    public int DailyReconHourLocal { get; set; } = 21;

    /// <summary>
    /// FulfillmentUnits whose last scan is older than this many hours are auto-added to the
    /// daily recon as 'missing' candidates. Default: 12 hours.
    /// </summary>
    public int ReconStaleHours { get; set; } = 12;

    // ── Subscription Billing / Dunning ──────────────────────────────────────────

    /// <summary>
    /// Master switch for the daily subscription billing + dunning job (ADR-010).
    /// Default: <c>false</c> — must be explicitly enabled.
    /// Set <c>Worker:SubscriptionBillingEnabled=true</c> to activate.
    /// </summary>
    public bool SubscriptionBillingEnabled { get; set; } = false;

    /// <summary>
    /// How often the subscription billing job polls (seconds). Default: 86400 (once/day).
    /// In Development, set to a short value for fast-cycle testing.
    /// </summary>
    public int SubscriptionBillingPollIntervalSeconds { get; set; } = 86400;

    /// <summary>
    /// How many failed dunning attempts before a customer subscription is suspended.
    /// Default: 3.
    /// </summary>
    public int SubscriptionMaxDunningAttempts { get; set; } = 3;

    /// <summary>
    /// Base interval (minutes) for dunning retry backoff.
    /// Retry N is scheduled at now + N * SubscriptionDunningBackoffMinutes.
    /// Default: 1440 (24 hours). So attempt 1 retries after 24 h, attempt 2 after 48 h.
    /// </summary>
    public int SubscriptionDunningBackoffMinutes { get; set; } = 1440;

    // ── Brand platform billing (the tenant's own platform tier) ────────────────────

    /// <summary>Master switch for the brand platform-subscription renewal billing job.
    /// Default <c>false</c>; set <c>Worker:BrandPlatformBillingEnabled=true</c> to enable.
    /// (The first invoice is issued synchronously by ApplyBundleToBrand regardless of this flag.)</summary>
    public bool BrandPlatformBillingEnabled { get; set; } = false;

    /// <summary>Poll interval (seconds) for the brand platform billing job. Default 86400 (daily).</summary>
    public int BrandPlatformBillingPollIntervalSeconds { get; set; } = 86400;

    // ── Auto-Dispatch ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Master switch for the auto-dispatch job.
    /// Default: <c>false</c> — must be explicitly enabled in appsettings or env vars.
    /// Set <c>AutoDispatch:Enabled=true</c> in <c>appsettings.Development.json</c> to
    /// test locally. See PRODUCTION_ENV.md for the production env-var form.
    /// </summary>
    public bool AutoDispatchEnabled { get; set; } = false;

    /// <summary>How often the auto-dispatch job polls for unassigned work (seconds). Default: 30.</summary>
    public int AutoDispatchPollSeconds { get; set; } = 30;

    /// <summary>
    /// Minimum age (minutes) a pending pickup_request must be before the auto-dispatcher
    /// will claim it. Default: 2 minutes, so admins can manually assign immediately
    /// created requests before the job takes over.
    /// </summary>
    public int AutoDispatchMinAgeMinutes { get; set; } = 2;

    /// <summary>Maximum number of pickup requests auto-assigned per poll cycle. Default: 10.</summary>
    public int AutoDispatchMaxPerCycle { get; set; } = 10;

    // ── DEFECT 5b: daily-partition maintenance ────────────────────────────────────

    /// <summary>
    /// Master switch for the partition-maintenance job that pre-creates upcoming daily
    /// partitions for <c>logistics.rider_location_pings</c>. Enabled by default — the
    /// original root cause of the broken location ping was that NO such job existed and
    /// partitions silently ran out, so rows fell into (or failed against) the DEFAULT
    /// partition. Disable only if an external scheduler (e.g. pg_partman) owns this.
    /// </summary>
    public bool PartitionMaintenanceEnabled { get; set; } = true;

    /// <summary>How often the partition-maintenance job runs (seconds). Default: 86400 (daily).</summary>
    public int PartitionMaintenanceIntervalSeconds { get; set; } = 86400;

    /// <summary>
    /// How many days of future daily partitions to keep provisioned ahead of "today"
    /// (store-local IST). Default: 14. Each run idempotently creates any missing day
    /// from today through today+N.
    /// </summary>
    public int PartitionMaintenanceDaysAhead { get; set; } = 14;
}
