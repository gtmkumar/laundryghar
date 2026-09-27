namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Who the notification is addressed to (notifications_outbox.recipient_type).
/// CHECK: recipient_type IN ('customer','user','rider','franchise_owner','manual')
/// notifications_log.recipient_type has no DB CHECK constraint; uses the same value set.
///
/// <para>Audit finding A-3 ("naming drift"): <see cref="FranchiseOwner"/> was spelled
/// <c>franchisee</c> here while <see cref="UserType.FranchiseOwner"/> spelled the same real person
/// <c>franchise_owner</c> — and the raw value reaches users, since admin-web's Notification Outbox
/// and Logs tabs render it directly when a recipient has no phone or email. Aligned by migration
/// 0033, which moves the CHECK constraint with it.</para>
///
/// <para>The <c>franchisee_*</c> columns on <c>tenancy_org.franchise_agreements</c> are deliberately
/// unchanged: there the word names the contracting party in a legal instrument, which is the correct
/// term for that job.</para>
/// </summary>
public static class NotificationRecipientType
{
    public const string Customer    = "customer";
    public const string User        = "user";
    public const string Rider       = "rider";
    public const string FranchiseOwner = "franchise_owner";
    public const string Manual      = "manual";
}
