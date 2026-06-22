using laundryghar.SharedDataModel.Enums;
using operations.Application.Logistics.Common;

namespace operations.Application.Fulfillment.Laundry;

/// <summary>
/// The laundry <c>process_deliver</c> fulfilment strategy and the regression baseline for
/// the whole seam. It owns no new logic in Phase 0 — every method delegates to the existing
/// <see cref="OrderStateMachine"/> (the current source of truth for laundry transitions), so
/// the extraction is provably behaviour-preserving. Phase 1 inlines the wash/QC pipeline here
/// and severs it from the shared <c>Order</c> aggregate.
/// </summary>
public sealed class LaundryProcessStrategy : IFulfillmentStrategy
{
    // Fully qualified to avoid clashing with the IFulfillmentStrategy.VerticalKey member below.
    public string VerticalKey => laundryghar.SharedDataModel.Enums.VerticalKey.Laundry;

    public string InitialStatus => OrderStatus.Placed;

    private static readonly IReadOnlySet<string> Terminals = new HashSet<string>
    {
        OrderStatus.Delivered, OrderStatus.Cancelled, OrderStatus.Closed, OrderStatus.Returned,
    };
    public IReadOnlySet<string> TerminalStatuses => Terminals;

    // The laundry status vocabulary = the spine OrderStatus values the laundry machine walks.
    private static readonly string[] KnownStatuses =
    [
        OrderStatus.Placed, OrderStatus.PickupScheduled, OrderStatus.PickupAssigned,
        OrderStatus.PickupInProgress, OrderStatus.PickedUp, OrderStatus.Received,
        OrderStatus.Sorting, OrderStatus.InProcess, OrderStatus.Qc, OrderStatus.Ready,
        OrderStatus.DeliveryScheduled, OrderStatus.DeliveryAssigned, OrderStatus.OutForDelivery,
        OrderStatus.Delivered, OrderStatus.Rewash, OrderStatus.Returned, OrderStatus.Disputed,
        OrderStatus.Cancelled, OrderStatus.Closed,
    ];

    public IReadOnlyDictionary<string, IReadOnlySet<string>> GetTransitions()
        => KnownStatuses.ToDictionary(
            s => s,
            s => (IReadOnlySet<string>)OrderStateMachine.AllowedNext(s, JobType.Laundry));

    public IReadOnlyList<string> GetHappyPath()
    {
        // Reconstructed from the existing happy path so there is a single source of truth.
        var path = new List<string> { OrderStatus.Placed };
        path.AddRange(OrderStateMachine.ForwardPath(OrderStatus.Placed, OrderStatus.Closed, JobType.Laundry));
        return path;
    }

    public bool IsKnownStatus(string status) => KnownStatuses.Contains(status);

    public bool ValidateTransition(string from, string to)
        => OrderStateMachine.AllowedNext(from, JobType.Laundry).Contains(to);

    public IReadOnlyList<string> ForwardPath(string from, string target)
        => OrderStateMachine.ForwardPath(from, target, JobType.Laundry);
}
