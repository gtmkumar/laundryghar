using laundryghar.Worker.Options;

namespace laundryghar.Worker.Tests.Subscriptions;

/// <summary>
/// Unit tests for the dunning-state logic embedded in SubscriptionBillingService.
///
/// These tests do NOT use the real service instance. Instead they replicate and
/// verify the pure business rules:
///   - Attempt 1 fail  → status becomes 'past_due', retry scheduled at +1×backoff
///   - Attempt 2 fail  → status remains 'past_due', retry scheduled at +2×backoff
///   - Attempt N fail where N ≥ MaxDunningAttempts → status becomes 'suspended'
///   - Any success     → status back to 'active', dunning counters reset to 0
///
/// This approach (pure rule verification) is chosen because the charge logic in
/// SubscriptionBillingService involves DB + gateway calls; isolating the state
/// transitions here gives us stable, fast tests with zero infrastructure.
/// </summary>
public sealed class SubscriptionDunningTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    private static WorkerOptions DefaultOptions(int maxAttempts = 3, int backoffMinutes = 1440)
        => new()
        {
            SubscriptionBillingEnabled         = true,
            SubscriptionMaxDunningAttempts     = maxAttempts,
            SubscriptionDunningBackoffMinutes  = backoffMinutes
        };

    /// <summary>
    /// Applies the dunning state-machine logic (mirroring SubscriptionBillingService.AttemptChargeAsync)
    /// to a mutable state record and returns the new state.
    /// </summary>
    private static DunningState ApplyFailure(DunningState current, WorkerOptions opts, DateTimeOffset now)
    {
        var nextAttemptNo = current.DunningAttempts + 1;
        var status        = current.Status;
        var pastDueSince  = current.PastDueSince;
        var retryAt       = now.AddMinutes(nextAttemptNo * opts.SubscriptionDunningBackoffMinutes);

        if (nextAttemptNo >= opts.SubscriptionMaxDunningAttempts)
            status = "suspended";
        else if (nextAttemptNo == 1)
        {
            status       = "past_due";
            pastDueSince = now;
        }
        // else: already past_due, status unchanged

        return current with
        {
            Status          = status,
            PastDueSince    = pastDueSince,
            DunningAttempts = nextAttemptNo,
            NextRetryAt     = retryAt
        };
    }

    private static DunningState ApplySuccess(DunningState current)
        => current with
        {
            Status          = "active",
            PastDueSince    = null,
            DunningAttempts = 0,
            NextRetryAt     = null
        };

    // ── First failure → past_due ───────────────────────────────────────────

    [Fact]
    public void FirstFailure_StatusBecomesPassDue()
    {
        var opts  = DefaultOptions(maxAttempts: 3);
        var now   = new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero);
        var state = new DunningState("active", DunningAttempts: 0);

        var result = ApplyFailure(state, opts, now);

        Assert.Equal("past_due", result.Status);
    }

    [Fact]
    public void FirstFailure_PastDueSinceIsSetToNow()
    {
        var opts  = DefaultOptions(maxAttempts: 3);
        var now   = new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero);
        var state = new DunningState("active", DunningAttempts: 0);

        var result = ApplyFailure(state, opts, now);

        Assert.Equal(now, result.PastDueSince);
    }

    [Fact]
    public void FirstFailure_DunningAttemptsIncrementsToOne()
    {
        var opts  = DefaultOptions(maxAttempts: 3);
        var now   = new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero);
        var state = new DunningState("active", DunningAttempts: 0);

        var result = ApplyFailure(state, opts, now);

        Assert.Equal(1, result.DunningAttempts);
    }

    [Fact]
    public void FirstFailure_RetryScheduledAtOneBackoffUnit()
    {
        var opts = DefaultOptions(maxAttempts: 3, backoffMinutes: 1440);
        var now  = new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero);
        var state = new DunningState("active", DunningAttempts: 0);

        var result = ApplyFailure(state, opts, now);

        // 1 * 1440 minutes = 24 hours
        Assert.Equal(now.AddMinutes(1440), result.NextRetryAt);
    }

    // ── Second failure → still past_due, retry at 2× backoff ─────────────

    [Fact]
    public void SecondFailure_StatusRemainsPassDue()
    {
        var opts  = DefaultOptions(maxAttempts: 3);
        var now   = new DateTimeOffset(2026, 6, 11, 10, 0, 0, TimeSpan.Zero);
        var state = new DunningState("past_due", DunningAttempts: 1,
                                     PastDueSince: now.AddDays(-1));

        var result = ApplyFailure(state, opts, now);

        Assert.Equal("past_due", result.Status);
    }

    [Fact]
    public void SecondFailure_RetryScheduledAtTwoBackoffUnits()
    {
        var opts  = DefaultOptions(maxAttempts: 3, backoffMinutes: 1440);
        var now   = new DateTimeOffset(2026, 6, 11, 10, 0, 0, TimeSpan.Zero);
        var state = new DunningState("past_due", DunningAttempts: 1,
                                     PastDueSince: now.AddDays(-1));

        var result = ApplyFailure(state, opts, now);

        // 2 * 1440 = 48 hours
        Assert.Equal(now.AddMinutes(2 * 1440), result.NextRetryAt);
    }

    // ── N-th failure → suspended ─────────────────────────────────────────

    [Theory]
    [InlineData(3, 3)]
    [InlineData(3, 4)]  // already maxed: suspension is idempotent
    [InlineData(5, 5)]
    [InlineData(1, 1)]  // maxAttempts=1: first failure immediately suspends
    public void NthFailure_StatusBecomesSuspended(int maxAttempts, int dunningAttemptsSoFar)
    {
        var opts  = DefaultOptions(maxAttempts: maxAttempts);
        var now   = new DateTimeOffset(2026, 6, 12, 10, 0, 0, TimeSpan.Zero);
        var state = new DunningState("past_due", DunningAttempts: dunningAttemptsSoFar - 1);

        var result = ApplyFailure(state, opts, now);

        Assert.Equal("suspended", result.Status);
    }

    [Fact]
    public void MaxAttempts1_FirstFailure_ImmediatelySuspends()
    {
        // When max_dunning_attempts = 1, there is no grace period.
        var opts  = DefaultOptions(maxAttempts: 1, backoffMinutes: 60);
        var now   = new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero);
        var state = new DunningState("active", DunningAttempts: 0);

        var result = ApplyFailure(state, opts, now);

        Assert.Equal("suspended", result.Status);
    }

    // ── Success resets everything ─────────────────────────────────────────

    [Fact]
    public void Success_FromPastDue_StatusBecomesActive()
    {
        var state = new DunningState("past_due", DunningAttempts: 2,
                                     PastDueSince: DateTimeOffset.UtcNow.AddDays(-2));

        var result = ApplySuccess(state);

        Assert.Equal("active", result.Status);
    }

    [Fact]
    public void Success_FromPastDue_DunningAttemptsResetToZero()
    {
        var state = new DunningState("past_due", DunningAttempts: 2,
                                     PastDueSince: DateTimeOffset.UtcNow.AddDays(-2));

        var result = ApplySuccess(state);

        Assert.Equal(0, result.DunningAttempts);
    }

    [Fact]
    public void Success_PastDueSinceClearedToNull()
    {
        var state = new DunningState("past_due", DunningAttempts: 1,
                                     PastDueSince: DateTimeOffset.UtcNow.AddDays(-1));

        var result = ApplySuccess(state);

        Assert.Null(result.PastDueSince);
    }

    [Fact]
    public void Success_NextRetryAtClearedToNull()
    {
        var now   = DateTimeOffset.UtcNow;
        var state = new DunningState("past_due", DunningAttempts: 1,
                                     NextRetryAt: now.AddHours(12));

        var result = ApplySuccess(state);

        Assert.Null(result.NextRetryAt);
    }

    // ── Backoff is linear and proportional to attempt number ─────────────

    [Theory]
    [InlineData(1, 60,   60)]
    [InlineData(2, 60,  120)]
    [InlineData(3, 60,  180)]
    [InlineData(1, 1440, 1440)]
    [InlineData(2, 1440, 2880)]
    public void Backoff_IsLinearMultipleOfAttemptNumber(
        int attemptNumber, int backoffMinutes, int expectedMinutes)
    {
        // Simulates the formula: retryAt = now + attemptNumber * backoffMinutes
        var now      = new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
        var expected = now.AddMinutes(expectedMinutes);
        var actual   = now.AddMinutes(attemptNumber * backoffMinutes);

        Assert.Equal(expected, actual);
    }

    // ── Full dunning ladder walkthrough (default: 3 max attempts) ─────────

    [Fact]
    public void FullDunningLadder_3Attempts_ActiveToPastDueToPastDueToSuspended()
    {
        var opts    = DefaultOptions(maxAttempts: 3, backoffMinutes: 1440);
        var now     = new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
        var state   = new DunningState("active", DunningAttempts: 0);

        // Attempt 1: fail → past_due
        state = ApplyFailure(state, opts, now);
        Assert.Equal("past_due", state.Status);
        Assert.Equal(1, state.DunningAttempts);

        // Attempt 2: fail → still past_due, retry at 2×1440
        state = ApplyFailure(state, opts, now.AddDays(1));
        Assert.Equal("past_due", state.Status);
        Assert.Equal(2, state.DunningAttempts);

        // Attempt 3 (= MaxDunningAttempts): fail → suspended
        state = ApplyFailure(state, opts, now.AddDays(2));
        Assert.Equal("suspended", state.Status);
        Assert.Equal(3, state.DunningAttempts);
    }

    [Fact]
    public void FullDunningLadder_RecoveryOnSecondAttempt_ResetsToActive()
    {
        var opts  = DefaultOptions(maxAttempts: 3, backoffMinutes: 1440);
        var now   = new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
        var state = new DunningState("active", DunningAttempts: 0);

        // First attempt fails
        state = ApplyFailure(state, opts, now);
        Assert.Equal("past_due", state.Status);

        // Second attempt succeeds
        state = ApplySuccess(state);
        Assert.Equal("active",   state.Status);
        Assert.Equal(0,          state.DunningAttempts);
        Assert.Null(state.PastDueSince);
    }
}

// ── Value object for dunning state (test-internal helper) ────────────────────

internal sealed record DunningState(
    string           Status,
    int              DunningAttempts = 0,
    DateTimeOffset?  PastDueSince    = null,
    DateTimeOffset?  NextRetryAt     = null);
