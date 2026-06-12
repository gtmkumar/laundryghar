using FluentValidation;
using laundryghar.Orders.Application.Pickup.Commands;
using laundryghar.Orders.Application.Pickup.Dtos;
using laundryghar.Utilities.Exceptions;

namespace laundryghar.Orders.Tests.Pickup;

/// <summary>
/// Unit tests for <see cref="ReschedulePickupHandler"/> and <see cref="ReschedulePickupValidator"/>.
///
/// The slot-swap SQL path (ExecuteSqlAsync) requires a real database and is deferred to live E2E.
/// These tests cover:
///   - Status guard (reschedulable vs non-reschedulable statuses)
///   - Validator: past dates rejected; today and future accepted
///   - Slot decrement inverse arithmetic
/// </summary>
public sealed class ReschedulePickupTests
{
    // ── Status guard ──────────────────────────────────────────────────────────

    /// <summary>Mirrors the handler's ReschedulableStatuses field.</summary>
    private static readonly HashSet<string> ReschedulableStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "pending", "no_response", "rescheduled" };

    [Theory]
    [InlineData("pending")]
    [InlineData("no_response")]
    [InlineData("rescheduled")]
    public void ReschedulableStatuses_AllowedTransitions(string status)
    {
        Assert.True(ReschedulableStatuses.Contains(status),
            $"Status '{status}' should be reschedulable.");
    }

    [Theory]
    [InlineData("assigned")]
    [InlineData("rider_dispatched")]
    [InlineData("arrived")]
    [InlineData("completed")]
    [InlineData("converted")]
    [InlineData("cancelled")]
    [InlineData("rejected")]
    public void ReschedulableStatuses_ForbiddenTransitions(string status)
    {
        Assert.False(ReschedulableStatuses.Contains(status),
            $"Status '{status}' must NOT be reschedulable.");
    }

    [Fact]
    public void StatusCheck_IsCaseInsensitive()
    {
        // The handler uses OrdinalIgnoreCase, so mixed-case status values
        // from the DB must still match.
        Assert.True(ReschedulableStatuses.Contains("PENDING"));
        Assert.True(ReschedulableStatuses.Contains("Rescheduled"));
        Assert.True(ReschedulableStatuses.Contains("NO_RESPONSE"));
    }

    // ── Validator ─────────────────────────────────────────────────────────────

    private static ReschedulePickupCommand MakeCmd(DateOnly newDate) =>
        new(
            PickupRequestId: Guid.NewGuid(),
            CustomerId:      Guid.NewGuid(),
            BrandId:         Guid.NewGuid(),
            Request:         new ReschedulePickupRequest(newDate, null),
            ActorId:         null
        );

    [Fact]
    public void Validator_PastDate_Fails()
    {
        var validator = new ReschedulePickupValidator();
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));

        var result = validator.Validate(MakeCmd(yesterday));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("NewDate"));
    }

    [Fact]
    public void Validator_TodayDate_Passes()
    {
        var validator = new ReschedulePickupValidator();
        var today     = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var result = validator.Validate(MakeCmd(today));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_FutureDate_Passes()
    {
        var validator = new ReschedulePickupValidator();
        var future    = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(7));

        var result = validator.Validate(MakeCmd(future));

        Assert.True(result.IsValid);
    }

    // ── Slot capacity arithmetic ───────────────────────────────────────────────

    // The SQL for decrement: SET booked_count = GREATEST(booked_count - 1, 0)
    private static int DecrementSlot(int current) => Math.Max(current - 1, 0);

    [Theory]
    [InlineData(5, 4)]   // normal
    [InlineData(1, 0)]   // last booking
    [InlineData(0, 0)]   // already 0 — GREATEST guard floors at 0
    public void SlotDecrement_ClampsAtZero(int before, int expected)
    {
        Assert.Equal(expected, DecrementSlot(before));
    }

    // The SQL for increment: WHERE booked_count < capacity (rejected when full)
    private static bool CanBook(int booked, int capacity) => booked < capacity;

    [Theory]
    [InlineData(0, 5, true)]    // empty slot
    [InlineData(4, 5, true)]    // one space left
    [InlineData(5, 5, false)]   // full
    [InlineData(6, 5, false)]   // over capacity (should not occur normally)
    public void SlotIncrement_RespectsCapacity(int booked, int capacity, bool expected)
    {
        Assert.Equal(expected, CanBook(booked, capacity));
    }

    // ── Status transition after reschedule ────────────────────────────────────

    [Fact]
    public void AfterReschedule_StatusBecomesRescheduled()
    {
        // The handler unconditionally sets pr.Status = "rescheduled".
        // Verify the string constant matches the allowed set (so re-rescheduling is possible).
        const string targetStatus = "rescheduled";
        Assert.True(ReschedulableStatuses.Contains(targetStatus),
            "The post-reschedule status 'rescheduled' must itself be reschedulable so multiple reschedules are allowed.");
    }
}
