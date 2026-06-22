using laundryghar.SharedDataModel.Enums;

namespace operations.Application.Fulfillment.Laundry;

/// <summary>
/// The laundry <c>process_deliver</c> fulfilment strategy and the regression baseline for the
/// whole seam. The transition graph + happy path below were moved VERBATIM from the former
/// <c>OrderStateMachine.AllowedTransitions</c> / <c>HappyPath</c> (laundry wash/QC pipeline), so
/// the extraction is behaviour-preserving. Phase 2 inlines garment/warehouse linkage here and
/// severs it from the shared <c>Order</c> aggregate.
/// </summary>
public sealed class LaundryProcessStrategy : StateMachineStrategyBase
{
    public override string FulfillmentMode => laundryghar.SharedDataModel.Enums.FulfillmentMode.ProcessDeliver;
    public override string InitialStatus => OrderStatus.Placed;
    public override IReadOnlySet<string> TerminalStatuses => Terminals;

    protected override IReadOnlyDictionary<string, IReadOnlySet<string>> Transitions => Map;
    protected override IReadOnlyList<string> HappyPath => Path;

    private static readonly IReadOnlySet<string> Terminals = new HashSet<string>
    {
        OrderStatus.Delivered, OrderStatus.Cancelled, OrderStatus.Closed, OrderStatus.Returned,
    };

    // Verbatim from OrderStateMachine.AllowedTransitions (PRODUCTION_SPEC §4.1).
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Map =
        new Dictionary<string, IReadOnlySet<string>>
        {
            [OrderStatus.Placed]            = new HashSet<string> { OrderStatus.PickupScheduled, OrderStatus.Cancelled, OrderStatus.Disputed },
            [OrderStatus.PickupScheduled]   = new HashSet<string> { OrderStatus.PickupAssigned, OrderStatus.Cancelled, OrderStatus.Disputed },
            [OrderStatus.PickupAssigned]    = new HashSet<string> { OrderStatus.PickupInProgress, OrderStatus.Cancelled, OrderStatus.Disputed },
            [OrderStatus.PickupInProgress]  = new HashSet<string> { OrderStatus.PickedUp, OrderStatus.Cancelled, OrderStatus.Disputed },
            [OrderStatus.PickedUp]          = new HashSet<string> { OrderStatus.Received, OrderStatus.Disputed },
            [OrderStatus.Received]          = new HashSet<string> { OrderStatus.Sorting, OrderStatus.Disputed },
            [OrderStatus.Sorting]           = new HashSet<string> { OrderStatus.InProcess, OrderStatus.Disputed },
            [OrderStatus.InProcess]         = new HashSet<string> { OrderStatus.Qc, OrderStatus.Disputed },
            [OrderStatus.Qc]                = new HashSet<string> { OrderStatus.Ready, OrderStatus.Rewash, OrderStatus.Disputed },
            [OrderStatus.Ready]             = new HashSet<string> { OrderStatus.DeliveryScheduled, OrderStatus.Returned, OrderStatus.Disputed },
            [OrderStatus.DeliveryScheduled] = new HashSet<string> { OrderStatus.DeliveryAssigned, OrderStatus.Returned, OrderStatus.Disputed },
            [OrderStatus.DeliveryAssigned]  = new HashSet<string> { OrderStatus.OutForDelivery, OrderStatus.Returned, OrderStatus.Disputed },
            [OrderStatus.OutForDelivery]    = new HashSet<string> { OrderStatus.Delivered, OrderStatus.Returned, OrderStatus.Disputed },
            [OrderStatus.Delivered]         = new HashSet<string> { OrderStatus.Closed, OrderStatus.Rewash, OrderStatus.Disputed },
            [OrderStatus.Rewash]            = new HashSet<string> { OrderStatus.Sorting, OrderStatus.Disputed },
            [OrderStatus.Returned]          = new HashSet<string> { OrderStatus.Closed },
            [OrderStatus.Disputed]          = new HashSet<string> { OrderStatus.Closed, OrderStatus.InProcess },
            [OrderStatus.Cancelled]         = new HashSet<string>(),
            [OrderStatus.Closed]            = new HashSet<string>(),
        };

    // Verbatim from OrderStateMachine.HappyPath.
    private static readonly string[] Path =
    [
        OrderStatus.Placed,
        OrderStatus.PickupScheduled,
        OrderStatus.PickupAssigned,
        OrderStatus.PickupInProgress,
        OrderStatus.PickedUp,
        OrderStatus.Received,
        OrderStatus.Sorting,
        OrderStatus.InProcess,
        OrderStatus.Qc,
        OrderStatus.Ready,
        OrderStatus.DeliveryScheduled,
        OrderStatus.DeliveryAssigned,
        OrderStatus.OutForDelivery,
        OrderStatus.Delivered,
        OrderStatus.Closed,
    ];
}
