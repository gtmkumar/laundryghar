namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Matches finance_royalty.royalty_calculations.revenue_type CHECK constraint exactly.
/// Values: order, package, adjustment, refund.
/// </summary>
public enum RoyaltyCalculationRevenueType
{
    Order,
    Package,
    Adjustment,
    Refund
}
