using laundryghar.SharedDataModel.Enums;

namespace operations.Application.Fulfillment.Recurring;

/// <summary>
/// The <c>recurring</c> fulfilment strategy — PLATFORM_STRATEGY.md §3 Mode 3 ("repeat delivery
/// calendar: tiffin · milk · water cans").
///
/// <para>Each occurrence is an ordinary order, so this is deliberately the SIMPLEST of the four
/// strategies: there is nothing to collect and nothing to process. A meal leaves the kitchen and
/// arrives. The whole recurring-ness lives in
/// <c>order_lifecycle.delivery_schedules</c>, which emits these orders — by the time one exists it
/// is just a delivery.</para>
///
/// <para>Contrast with <c>point_to_point</c>, which looks similar but is not: a courier trip
/// COLLECTS from a sender first, so it carries a pickup leg. Here the goods start at the brand's own
/// kitchen, so there is no pickup at all — which is exactly why it needs its own strategy rather
/// than reusing the logistics one.</para>
/// </summary>
public sealed class RecurringDeliveryStrategy : StateMachineStrategyBase
{
    public override string FulfillmentMode => laundryghar.SharedDataModel.Enums.FulfillmentMode.Recurring;
    public override string InitialStatus => OrderStatus.Placed;
    public override IReadOnlySet<string> TerminalStatuses => Terminals;

    protected override IReadOnlyDictionary<string, IReadOnlySet<string>> Transitions => Map;
    protected override IReadOnlyList<string> HappyPath => Path;

    // Outbound only: the goods originate at the brand's own location, so there is no pickup leg and
    // nothing is ever dropped at a store for processing.
    public override string PostPickupStatus => OrderStatus.OutForDelivery;
    public override bool RequiresStoreDrop => false;
    public override FulfilmentLegs ResolveLegs(bool requestedPickup, bool requestedDelivery)
        => new(RequiresPickup: false, RequiresDelivery: true);

    private static readonly IReadOnlySet<string> Terminals = new HashSet<string>
    {
        OrderStatus.Delivered, OrderStatus.Cancelled, OrderStatus.Closed, OrderStatus.Returned,
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Map =
        new Dictionary<string, IReadOnlySet<string>>
        {
            // A generated occurrence goes straight to being scheduled for delivery — there is no
            // intake, no processing, no QC.
            [OrderStatus.Placed]            = new HashSet<string> { OrderStatus.DeliveryScheduled, OrderStatus.Cancelled, OrderStatus.Disputed },
            [OrderStatus.DeliveryScheduled] = new HashSet<string> { OrderStatus.OutForDelivery, OrderStatus.Cancelled, OrderStatus.Disputed },
            [OrderStatus.OutForDelivery]    = new HashSet<string> { OrderStatus.Delivered, OrderStatus.Returned, OrderStatus.Disputed },
            [OrderStatus.Delivered]         = new HashSet<string> { OrderStatus.Closed, OrderStatus.Disputed },
            // A customer not at home is routine for a standing daily delivery, not an exception —
            // it returns and the schedule simply produces tomorrow's order as normal.
            [OrderStatus.Returned]          = new HashSet<string> { OrderStatus.Closed },
            [OrderStatus.Disputed]          = new HashSet<string> { OrderStatus.Closed },
            [OrderStatus.Cancelled]         = new HashSet<string>(),
            [OrderStatus.Closed]            = new HashSet<string>(),
        };

    private static readonly string[] Path =
    [
        OrderStatus.Placed,
        OrderStatus.DeliveryScheduled,
        OrderStatus.OutForDelivery,
        OrderStatus.Delivered,
        OrderStatus.Closed,
    ];
}
