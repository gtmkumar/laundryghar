using laundryghar.SharedDataModel.Enums;

namespace operations.Application.Fulfillment.Logistics;

/// <summary>
/// The logistics <c>point_to_point</c> fulfilment strategy (a single A→B trip). The transition
/// graph + happy path were moved VERBATIM from the former <c>OrderStateMachine.ParcelTransitions</c>
/// / <c>ParcelHappyPath</c> — they skip the laundry-only intake/processing states
/// (received → sorting → in_process → qc → ready) and the separate delivery-assignment leg.
/// This strategy serves today's <c>JobType.Parcel</c> orders and is the proof the seam generalizes.
/// </summary>
public sealed class LogisticsPointToPointStrategy : StateMachineStrategyBase
{
    public override string FulfillmentMode => laundryghar.SharedDataModel.Enums.FulfillmentMode.PointToPoint;
    public override string InitialStatus => OrderStatus.Placed;
    public override IReadOnlySet<string> TerminalStatuses => Terminals;

    protected override IReadOnlyDictionary<string, IReadOnlySet<string>> Transitions => Map;
    protected override IReadOnlyList<string> HappyPath => Path;

    // A direct A→B trip: no store drop, pickup completion goes straight to out_for_delivery
    // (skipping laundry intake/processing). Both legs are always required.
    public override string PostPickupStatus => OrderStatus.OutForDelivery;
    public override bool RequiresStoreDrop => false;
    public override FulfilmentLegs ResolveLegs(bool requestedPickup, bool requestedDelivery)
        => new(RequiresPickup: true, RequiresDelivery: true);

    private static readonly IReadOnlySet<string> Terminals = new HashSet<string>
    {
        OrderStatus.Delivered, OrderStatus.Cancelled, OrderStatus.Closed, OrderStatus.Returned,
    };

    // Verbatim from OrderStateMachine.ParcelTransitions.
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Map =
        new Dictionary<string, IReadOnlySet<string>>
        {
            [OrderStatus.Placed]            = new HashSet<string> { OrderStatus.PickupScheduled, OrderStatus.Cancelled, OrderStatus.Disputed },
            [OrderStatus.PickupScheduled]   = new HashSet<string> { OrderStatus.PickupAssigned, OrderStatus.Cancelled, OrderStatus.Disputed },
            [OrderStatus.PickupAssigned]    = new HashSet<string> { OrderStatus.PickupInProgress, OrderStatus.Cancelled, OrderStatus.Disputed },
            [OrderStatus.PickupInProgress]  = new HashSet<string> { OrderStatus.PickedUp, OrderStatus.Cancelled, OrderStatus.Disputed },
            [OrderStatus.PickedUp]          = new HashSet<string> { OrderStatus.OutForDelivery, OrderStatus.Disputed },
            [OrderStatus.OutForDelivery]    = new HashSet<string> { OrderStatus.Delivered, OrderStatus.Returned, OrderStatus.Disputed },
            [OrderStatus.Delivered]         = new HashSet<string> { OrderStatus.Closed, OrderStatus.Disputed },
            [OrderStatus.Returned]          = new HashSet<string> { OrderStatus.Closed },
            [OrderStatus.Disputed]          = new HashSet<string> { OrderStatus.Closed },
            [OrderStatus.Cancelled]         = new HashSet<string>(),
            [OrderStatus.Closed]            = new HashSet<string>(),
        };

    // Verbatim from OrderStateMachine.ParcelHappyPath.
    private static readonly string[] Path =
    [
        OrderStatus.Placed,
        OrderStatus.PickupScheduled,
        OrderStatus.PickupAssigned,
        OrderStatus.PickupInProgress,
        OrderStatus.PickedUp,
        OrderStatus.OutForDelivery,
        OrderStatus.Delivered,
        OrderStatus.Closed,
    ];
}
