using laundryghar.Orders.Infrastructure.Auth;
using laundryghar.Orders.Infrastructure.Services;
namespace laundryghar.Orders.Application.Common;

/// <summary>
/// Computes the promised delivery timestamp (TAT engine).
///
/// TAT rule (documented for ops team):
///   1. For each order item, look up the linked service's TAT:
///      - express order → Service.ExpressTatHours
///      - standard order → Service.BaseTatHours
///   2. Take MAX across all items' TAT values (worst-case service drives the promise).
///   3. If no service TAT is resolvable (items list empty or all services missing),
///      fall back to <see cref="OrdersSettings.ExpressTatHours"/> or
///      <see cref="OrdersSettings.DefaultTatHours"/> respectively.
///   4. Add the resolved hours to <paramref name="placedAt"/> (UTC).
///
/// This is a pure function — no I/O — so it is trivially unit-testable.
/// </summary>
public static class TatCalculator
{
    /// <param name="placedAt">UTC moment the order was placed.</param>
    /// <param name="isExpress">True for express orders.</param>
    /// <param name="servicesTat">
    ///   Per-service TAT hours already resolved from the catalog.
    ///   Pass an empty span when services are unknown (legacy path).
    /// </param>
    /// <param name="settings">Bound <see cref="OrdersSettings"/> instance.</param>
    /// <returns>The promised delivery DateTimeOffset (UTC).</returns>
    public static DateTimeOffset Compute(
        DateTimeOffset placedAt,
        bool isExpress,
        ReadOnlySpan<int> servicesTat,
        OrdersSettings settings)
    {
        int tatHours = ResolveHours(isExpress, servicesTat, settings);
        return placedAt.AddHours(tatHours);
    }

    /// <summary>Resolves the effective TAT in hours without adding to a timestamp.
    /// Exposed separately so unit tests can assert the hour value independently.</summary>
    public static int ResolveHours(
        bool isExpress,
        ReadOnlySpan<int> servicesTat,
        OrdersSettings settings)
    {
        if (servicesTat.IsEmpty)
            return isExpress ? settings.ExpressTatHours : settings.DefaultTatHours;

        int max = 0;
        foreach (var h in servicesTat)
            if (h > max) max = h;

        // If catalog returned 0 for every service fall back to config defaults.
        return max > 0 ? max : (isExpress ? settings.ExpressTatHours : settings.DefaultTatHours);
    }
}
