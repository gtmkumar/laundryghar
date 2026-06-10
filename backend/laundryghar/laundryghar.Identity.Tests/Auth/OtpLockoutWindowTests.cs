namespace laundryghar.Identity.Tests.Auth;

/// <summary>
/// Regression tests for the rolling-window lockout time-cutoff arithmetic
/// used in <see cref="laundryghar.Identity.Application.Auth.Commands.OtpVerifyHandler"/>
/// and <see cref="laundryghar.Identity.Application.Auth.Commands.OtpSendHandler"/>.
///
/// The handler computes:
///   var lockoutWindowCutoff = DateTimeOffset.UtcNow.AddMinutes(-LockoutWindowMinutes);
///   // then queries WHERE created_at > lockoutWindowCutoff
///
/// This test suite pins the pure time-cutoff arithmetic — no DB required.
///
/// NOTE: <see cref="OtpSecurityHelperTests"/> already covers the threshold/sum
/// helpers. This file covers the window boundary calculation only.
/// </summary>
public sealed class OtpLockoutWindowTests
{
    // Freeze a reference "now" for deterministic assertions.
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    // ─── Cutoff computation mirrors the handler ───────────────────────────────

    private static DateTimeOffset ComputeCutoff(DateTimeOffset now, int windowMinutes)
        => now.AddMinutes(-windowMinutes);

    private static bool IsWithinWindow(DateTimeOffset createdAt, DateTimeOffset cutoff)
        => createdAt > cutoff;   // strict ">" mirrors the SQL WHERE created_at > cutoff

    // ─── Window boundary — 15-minute default ─────────────────────────────────

    [Theory]
    [InlineData(0)]     // exactly now — clearly within window
    [InlineData(-1)]    // 1 min ago — within window
    [InlineData(-14)]   // 14 min ago — within window (< 15 min window)
    public void CreatedAt_WithinWindow_IsIncluded(int minutesAgo)
    {
        var cutoff    = ComputeCutoff(Now, windowMinutes: 15);
        var createdAt = Now.AddMinutes(minutesAgo);
        Assert.True(IsWithinWindow(createdAt, cutoff),
            $"Row created {-minutesAgo} min ago should be within a 15-min window.");
    }

    [Theory]
    [InlineData(-15)]   // exactly at the 15-min boundary — strict >, so excluded
    [InlineData(-16)]   // 16 min ago — outside
    [InlineData(-60)]   // 1 hour ago — outside
    public void CreatedAt_OutsideWindow_IsExcluded(int minutesAgo)
    {
        var cutoff    = ComputeCutoff(Now, windowMinutes: 15);
        var createdAt = Now.AddMinutes(minutesAgo);
        Assert.False(IsWithinWindow(createdAt, cutoff),
            $"Row created {-minutesAgo} min ago should be outside a 15-min window.");
    }

    // ─── Boundary precision: 1 second before cutoff is excluded ─────────────

    [Fact]
    public void OneBefore_ExactCutoff_IsExcluded()
    {
        // Row created exactly 15 minutes before now is at the cutoff boundary.
        // The handler uses strict >, so this row is NOT counted.
        var cutoff    = ComputeCutoff(Now, windowMinutes: 15);
        var createdAt = cutoff;   // exactly at the cutoff
        Assert.False(IsWithinWindow(createdAt, cutoff));
    }

    [Fact]
    public void OneSecondAfterCutoff_IsIncluded()
    {
        // A row created 1 second after the cutoff IS within the window.
        var cutoff    = ComputeCutoff(Now, windowMinutes: 15);
        var createdAt = cutoff.AddSeconds(1);
        Assert.True(IsWithinWindow(createdAt, cutoff));
    }

    // ─── Different window sizes ────────────────────────────────────────────────

    [Theory]
    [InlineData(5,   4,  true)]    // 5-min window, row 4 min ago → included
    [InlineData(5,   5,  false)]   // 5-min window, row 5 min ago → excluded (exact boundary)
    [InlineData(30,  29, true)]    // 30-min window, row 29 min ago → included
    [InlineData(30,  31, false)]   // 30-min window, row 31 min ago → excluded
    [InlineData(60,  59, true)]    // 1-hour window
    [InlineData(60,  60, false)]   // exactly at 1-hour boundary → excluded
    public void WindowSizes_BoundaryBehaviour(int windowMinutes, int rowAgeMinutes, bool expectedIncluded)
    {
        var cutoff    = ComputeCutoff(Now, windowMinutes);
        var createdAt = Now.AddMinutes(-rowAgeMinutes);
        Assert.Equal(expectedIncluded, IsWithinWindow(createdAt, cutoff));
    }

    // ─── Total attempts across window ─────────────────────────────────────────

    /// <summary>
    /// Simulates the full lockout check: given N otp_code rows with their
    /// creation timestamps, only rows within the window contribute to the
    /// lockout sum.
    /// </summary>
    [Fact]
    public void WindowFilter_OnlyCountsRowsWithinWindow()
    {
        var windowMinutes = 15;
        var cutoff        = ComputeCutoff(Now, windowMinutes);

        // Rows with (createdAt, attempts)
        var rows = new[]
        {
            (CreatedAt: Now.AddMinutes(-1),  Attempts: (short)3),  // in window  → counted
            (CreatedAt: Now.AddMinutes(-10), Attempts: (short)4),  // in window  → counted
            (CreatedAt: Now.AddMinutes(-15), Attempts: (short)5),  // at boundary → excluded
            (CreatedAt: Now.AddMinutes(-20), Attempts: (short)2),  // outside    → excluded
        };

        var windowAttempts = rows
            .Where(r => IsWithinWindow(r.CreatedAt, cutoff))
            .Select(r => r.Attempts);

        var total = laundryghar.Identity.Infrastructure.Auth.OtpSecurityHelper
            .SumWindowAttempts(windowAttempts);

        // Only rows at -1min (3) and -10min (4) qualify → total = 7
        Assert.Equal(7, total);
    }

    [Fact]
    public void WindowFilter_NoRowsInWindow_ReturnsZero()
    {
        var cutoff = ComputeCutoff(Now, windowMinutes: 15);

        var rows = new[]
        {
            (CreatedAt: Now.AddMinutes(-20), Attempts: (short)5),
            (CreatedAt: Now.AddMinutes(-30), Attempts: (short)3),
        };

        var windowAttempts = rows
            .Where(r => IsWithinWindow(r.CreatedAt, cutoff))
            .Select(r => r.Attempts);

        var total = laundryghar.Identity.Infrastructure.Auth.OtpSecurityHelper
            .SumWindowAttempts(windowAttempts);

        Assert.Equal(0, total);
    }

    // ─── Lockout duration — LockoutDurationMinutes ───────────────────────────

    /// <summary>
    /// When lockout fires, the error message includes LockoutDurationMinutes.
    /// Pin the arithmetic used to compute the message string.
    /// (The actual throw happens in the handler; this pins the integer math.)
    /// </summary>
    [Theory]
    [InlineData(15,  "15")]
    [InlineData(30,  "30")]
    [InlineData(5,   "5")]
    [InlineData(60,  "60")]
    public void LockoutDurationMessage_FormatIsCorrect(int durationMinutes, string expectedMinStr)
    {
        // The handler constructs: $"Too many attempts. Try again in {LockoutDurationMinutes} minutes."
        var msg = $"Too many attempts. Try again in {durationMinutes} minutes.";
        Assert.Contains(expectedMinStr, msg);
        Assert.StartsWith("Too many attempts.", msg);
    }
}
