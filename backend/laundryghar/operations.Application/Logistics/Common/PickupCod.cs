namespace operations.Application.Logistics.Common;

/// <summary>
/// COD-cash rule for a pickup leg. Cash is due only when the booking's payment preference is
/// "cod" and the estimated amount is positive — wallet/UPI-deferred pickups collect nothing.
///
/// <para>Pure, dependency-free rule mirrored verbatim from the legacy Orders
/// <c>AssignPickupHandler.ResolvePickupCodAmount</c>, so the rider collect-on-pickup path applies
/// the SAME rule as assign-time without referencing the Orders bounded context.</para>
/// </summary>
public static class PickupCod
{
    /// <summary>
    /// Resolves the cash due for a pickup leg from its booking's payment preference + estimate.
    /// Returns null for non-COD preferences or a non-positive estimate.
    /// </summary>
    public static decimal? ResolvePickupCodAmount(string? paymentPreference, decimal? estimatedAmount)
    {
        var isCod = string.Equals(paymentPreference?.Trim(), "cod", StringComparison.OrdinalIgnoreCase);
        if (!isCod) return null;
        var amount = estimatedAmount ?? 0m;
        return amount > 0m ? amount : null;
    }
}
