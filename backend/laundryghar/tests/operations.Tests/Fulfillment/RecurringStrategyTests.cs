using laundryghar.SharedDataModel.Enums;
using operations.Application.Fulfillment.Recurring;
using Xunit;

namespace operations.Tests.Fulfillment;

/// <summary>
/// The <c>recurring</c> fulfilment strategy (§3 Mode 3). The interesting property is what it does
/// NOT do: goods start at the brand's own kitchen, so unlike every other mode there is no pickup
/// leg and nothing is ever dropped at a store for processing. That is precisely why it cannot reuse
/// the logistics point-to-point strategy, which looks similar but collects from a sender first.
/// </summary>
public class RecurringStrategyTests
{
    private static readonly RecurringDeliveryStrategy Strategy = new();

    [Fact]
    public void it_serves_the_recurring_mode()
        => Assert.Equal(FulfillmentMode.Recurring, Strategy.FulfillmentMode);

    // 1 ── outbound only. Asking for a pickup does not get you one.
    [Fact]
    public void there_is_no_pickup_leg_even_if_one_is_requested()
    {
        var legs = Strategy.ResolveLegs(requestedPickup: true, requestedDelivery: true);

        Assert.False(legs.RequiresPickup);
        Assert.True(legs.RequiresDelivery);
        Assert.False(Strategy.RequiresStoreDrop);
    }

    // 2 ── the happy path skips intake, processing and QC entirely.
    [Fact]
    public void the_happy_path_is_place_schedule_deliver_close()
    {
        Assert.Equal(
            [OrderStatus.Placed, OrderStatus.DeliveryScheduled, OrderStatus.OutForDelivery,
             OrderStatus.Delivered, OrderStatus.Closed],
            Strategy.GetHappyPath());
    }

    [Theory]
    [InlineData(OrderStatus.Received)]
    [InlineData(OrderStatus.Sorting)]
    [InlineData(OrderStatus.InProcess)]
    [InlineData(OrderStatus.Qc)]
    [InlineData(OrderStatus.PickedUp)]
    public void laundry_pipeline_states_are_not_part_of_this_mode(string status)
        => Assert.False(Strategy.IsKnownStatus(status));

    // 3 ── a delivery that could not be handed over RETURNS; it does not fail the schedule. A
    //      customer being out is routine for a standing daily delivery, and tomorrow's order is
    //      generated as normal.
    [Fact]
    public void an_undelivered_occurrence_can_return_and_close()
    {
        Assert.True(Strategy.CanTransition(OrderStatus.OutForDelivery, OrderStatus.Returned));
        Assert.True(Strategy.CanTransition(OrderStatus.Returned, OrderStatus.Closed));
    }

    [Fact]
    public void terminal_states_go_nowhere()
    {
        Assert.Empty(Strategy.AllowedNext(OrderStatus.Cancelled));
        Assert.Empty(Strategy.AllowedNext(OrderStatus.Closed));
        Assert.Contains(OrderStatus.Delivered, Strategy.TerminalStatuses);
    }

    [Fact]
    public void an_illegal_transition_is_rejected()
        => Assert.ThrowsAny<Exception>(
            () => Strategy.EnsureTransition(OrderStatus.Placed, OrderStatus.Delivered));

    // 4 ── a customer can still cancel a single occurrence before it goes out.
    [Fact]
    public void an_occurrence_can_be_cancelled_before_dispatch()
    {
        Assert.True(Strategy.CanTransition(OrderStatus.Placed, OrderStatus.Cancelled));
        Assert.True(Strategy.CanTransition(OrderStatus.DeliveryScheduled, OrderStatus.Cancelled));
        Assert.False(Strategy.CanTransition(OrderStatus.Delivered, OrderStatus.Cancelled));
    }
}
