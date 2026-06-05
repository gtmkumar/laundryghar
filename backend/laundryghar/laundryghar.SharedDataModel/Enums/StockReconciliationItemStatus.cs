namespace laundryghar.SharedDataModel.Enums;

public static class StockReconciliationItemStatus
{
    public const string Matched = "matched";
    public const string Missing = "missing";
    public const string Unexpected = "unexpected";
    public const string Damaged = "damaged";
    public const string Resolved = "resolved";
    public const string Escalated = "escalated";
}
