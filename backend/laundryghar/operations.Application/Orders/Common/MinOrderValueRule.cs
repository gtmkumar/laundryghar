using System.Globalization;
using laundryghar.Utilities.Exceptions;
using operations.Application.Common.Interfaces;
using operations.Application.Common.Settings;

namespace operations.Application.Orders.Common;

/// <summary>
/// Enforces the effective <c>orders/min_order_value</c> business rule for an order's scope
/// (store → franchise → brand → platform precedence). The minimum has no default at any scope:
/// <b>unset ⇒ no restriction</b>. When set, the order's pre-tax, pre-delivery item subtotal must
/// meet or exceed it; otherwise a structured 422 (code <c>min_order_value_not_met</c>) carrying
/// <c>minimum</c>, <c>subtotal</c> and <c>shortfall</c> is raised. This is a hard block
/// (product decision) — the customer app pre-checks via the catalog config endpoint but the
/// server is the enforcement point.
/// </summary>
public static class MinOrderValueRule
{
    /// <summary>Machine-readable error code carried by the structured 422 when the rule is violated.</summary>
    public const string ErrorCode = "min_order_value_not_met";

    public static async Task EnforceAsync(
        IOperationsDbContext db, Guid brandId, Guid? franchiseId, Guid? storeId,
        decimal subtotal, CancellationToken ct)
    {
        var minimum = await SettingsResolver.GetDecimalAsync(
            db, brandId, franchiseId, storeId,
            SettingCategories.Orders, SettingKeys.MinOrderValue, ct);

        // Unset (or non-positive) ⇒ no restriction.
        if (minimum is null || minimum.Value <= 0m) return;
        if (subtotal >= minimum.Value) return;

        var shortfall = minimum.Value - subtotal;
        throw new StructuredBusinessRuleException(
            ErrorCode,
            $"Order total is below the minimum of {minimum.Value.ToString("0.##", CultureInfo.InvariantCulture)}. " +
            $"Add {shortfall.ToString("0.##", CultureInfo.InvariantCulture)} more to place this order.",
            new Dictionary<string, string>
            {
                ["minimum"]   = minimum.Value.ToString(CultureInfo.InvariantCulture),
                ["subtotal"]  = subtotal.ToString(CultureInfo.InvariantCulture),
                ["shortfall"] = shortfall.ToString(CultureInfo.InvariantCulture),
            });
    }
}
