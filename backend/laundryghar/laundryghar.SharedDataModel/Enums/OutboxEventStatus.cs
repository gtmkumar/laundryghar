namespace laundryghar.SharedDataModel.Enums;

public static class OutboxEventStatus
{
    public const string Pending = "pending";
    public const string Publishing = "publishing";
    public const string Published = "published";
    public const string Failed = "failed";
    public const string DeadLetter = "dead_letter";
}
