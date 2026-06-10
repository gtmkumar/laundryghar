using laundryghar.Orders.Application.Common;

namespace laundryghar.Orders.Tests.Ops;

/// <summary>
/// Unit tests for <see cref="TatCalculator"/> — pure logic, zero I/O.
///
/// TAT rule recap:
///   - MAX(service TAT) across all services on the order.
///   - Express order → ExpressTatHours from service (or config fallback).
///   - Standard order → BaseTatHours from service (or config fallback).
///   - Empty service list → config fallback.
///   - All-zero service TATs → config fallback.
///   - promised_at = placed_at + resolved_hours.
/// </summary>
public sealed class TatCalculatorTests
{
    private static OrdersSettings DefaultSettings() => new()
    {
        DefaultTatHours  = 48,
        ExpressTatHours  = 24,
        StuckThresholdHours = 24
    };

    // ── ResolveHours ──────────────────────────────────────────────────────────

    [Fact]
    public void ResolveHours_EmptyServices_Standard_ReturnsDefaultTatHours()
    {
        var hours = TatCalculator.ResolveHours(isExpress: false, servicesTat: [], DefaultSettings());
        Assert.Equal(48, hours);
    }

    [Fact]
    public void ResolveHours_EmptyServices_Express_ReturnsExpressTatHours()
    {
        var hours = TatCalculator.ResolveHours(isExpress: true, servicesTat: [], DefaultSettings());
        Assert.Equal(24, hours);
    }

    [Fact]
    public void ResolveHours_SingleService_ReturnsThatServiceTat()
    {
        var hours = TatCalculator.ResolveHours(isExpress: false, servicesTat: [36], DefaultSettings());
        Assert.Equal(36, hours);
    }

    [Fact]
    public void ResolveHours_MultipleServices_ReturnsMax()
    {
        // Three services: 24, 72, 48 → MAX = 72
        var hours = TatCalculator.ResolveHours(isExpress: false, servicesTat: [24, 72, 48], DefaultSettings());
        Assert.Equal(72, hours);
    }

    [Fact]
    public void ResolveHours_AllZeroServices_FallsBackToConfig()
    {
        // Services present but all return 0 (bad catalog data) → use config default
        var hours = TatCalculator.ResolveHours(isExpress: false, servicesTat: [0, 0], DefaultSettings());
        Assert.Equal(48, hours);
    }

    [Fact]
    public void ResolveHours_AllZeroServices_Express_FallsBackToExpressConfig()
    {
        var hours = TatCalculator.ResolveHours(isExpress: true, servicesTat: [0, 0], DefaultSettings());
        Assert.Equal(24, hours);
    }

    [Theory]
    [InlineData(new int[] { 24, 48 }, false, 48)]  // max of 24 & 48 standard → 48
    [InlineData(new int[] { 12, 6  }, true,  12)]  // max of 12 & 6 express  → 12
    [InlineData(new int[] { 96 },     false, 96)]  // single service, larger than default
    public void ResolveHours_Parametric(int[] tatValues, bool isExpress, int expected)
    {
        var hours = TatCalculator.ResolveHours(isExpress, tatValues, DefaultSettings());
        Assert.Equal(expected, hours);
    }

    // ── Compute ───────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_AddsHoursToPlacedAt()
    {
        var placedAt = new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero);
        var result   = TatCalculator.Compute(placedAt, isExpress: false, servicesTat: [48], DefaultSettings());

        Assert.Equal(placedAt.AddHours(48), result);
    }

    [Fact]
    public void Compute_Express_UsesExpressTat()
    {
        var placedAt = new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero);
        var result   = TatCalculator.Compute(placedAt, isExpress: true, servicesTat: [12], DefaultSettings());

        Assert.Equal(placedAt.AddHours(12), result);
    }

    [Fact]
    public void Compute_EmptyServices_UsesConfigDefault()
    {
        var placedAt = new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero);
        var result   = TatCalculator.Compute(placedAt, isExpress: false, servicesTat: [], DefaultSettings());

        Assert.Equal(placedAt.AddHours(48), result);
    }

    // ── Config override scenarios ────────────────────────────────────────────

    [Fact]
    public void ResolveHours_CustomConfig_RespectsOverriddenDefaults()
    {
        var settings = new OrdersSettings { DefaultTatHours = 72, ExpressTatHours = 36 };

        Assert.Equal(72, TatCalculator.ResolveHours(false, [], settings));
        Assert.Equal(36, TatCalculator.ResolveHours(true,  [], settings));
    }
}
