using ItemEntity = laundryghar.SharedDataModel.Entities.CustomerCatalog.Item;

namespace operations.Application.Catalog.Pricing.Common;

/// <summary>
/// The editable-field snapshot of a catalog <see cref="ItemEntity"/> captured in the pricing change
/// log (target_kind = 'item'). Only the fields an admin can edit are recorded — enough to show a
/// before/after diff in the Change history tab and to restore an UPDATE via Revert (GH #24 item audit).
/// </summary>
public sealed record ItemAuditState(
    string Name,
    string Code,
    Guid? ItemGroupId,
    string Status,
    int? TatHours,
    bool ExpressEligible,
    decimal? ExpressSurcharge,
    string PricingMode);

/// <summary>
/// Wraps an <see cref="ItemAuditState"/> with the operation that produced the log entry. The operation
/// makes create/update/delete self-describing so Revert can accept UPDATEs (restore <see cref="State"/>)
/// and reject create/delete entries as non-revertible. For a create the before-state is null; for a
/// delete the after-state is null.
/// </summary>
public sealed record ItemAuditEnvelope(string Op, ItemAuditState? State);

/// <summary>Helpers to snapshot an item and build the change-log envelopes.</summary>
public static class ItemAudit
{
    public const string OpCreate = "create";
    public const string OpUpdate = "update";
    public const string OpDelete = "delete";

    /// <summary>Captures the editable fields of an item into an immutable snapshot.</summary>
    public static ItemAuditState Capture(ItemEntity e) => new(
        e.Name, e.Code, e.ItemGroupId, e.Status, e.TatHours,
        e.ExpressEligible, e.ExpressSurcharge, e.PricingMode);
}
