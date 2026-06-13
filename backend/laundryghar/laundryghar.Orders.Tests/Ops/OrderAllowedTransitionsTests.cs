using laundryghar.Orders.Application.Common;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;

namespace laundryghar.Orders.Tests.Ops;

/// <summary>
/// Tests the source of the <c>allowedTransitions</c> field added to the admin/customer
/// order-detail response (DEFECT D): <see cref="OrderStateMachine.AllowedNext"/>. The
/// JSON contract is a string[] of status codes for the order's current status, and an
/// empty array for terminal states.
/// </summary>
public sealed class OrderAllowedTransitionsTests
{
    [Fact]
    public void Placed_OffersScheduleCancelDispute()
    {
        var next = OrderStateMachine.AllowedNext(OrderStatus.Placed);
        Assert.Equal(
            new[] { OrderStatus.PickupScheduled, OrderStatus.Cancelled, OrderStatus.Disputed }
                .OrderBy(s => s),
            next.OrderBy(s => s));
    }

    [Fact]
    public void Qc_OffersReadyRewashDispute()
    {
        var next = OrderStateMachine.AllowedNext(OrderStatus.Qc);
        Assert.Contains(OrderStatus.Ready, next);
        Assert.Contains(OrderStatus.Rewash, next);
        Assert.Contains(OrderStatus.Disputed, next);
        Assert.Equal(3, next.Count);
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Closed)]
    public void TerminalStatuses_YieldEmptyArray(string terminal)
        => Assert.Empty(OrderStateMachine.AllowedNext(terminal));

    [Fact]
    public void UnknownStatus_YieldsEmptyArray_NeverThrows()
        => Assert.Empty(OrderStateMachine.AllowedNext("not_a_real_status"));

    [Fact]
    public void EveryNonTerminalStatus_HasAtLeastOneTransition()
    {
        string[] terminal = { OrderStatus.Cancelled, OrderStatus.Closed };
        string[] all =
        {
            OrderStatus.Placed, OrderStatus.PickupScheduled, OrderStatus.PickupAssigned,
            OrderStatus.PickupInProgress, OrderStatus.PickedUp, OrderStatus.Received,
            OrderStatus.Sorting, OrderStatus.InProcess, OrderStatus.Qc, OrderStatus.Ready,
            OrderStatus.DeliveryScheduled, OrderStatus.DeliveryAssigned,
            OrderStatus.OutForDelivery, OrderStatus.Delivered, OrderStatus.Rewash,
            OrderStatus.Returned, OrderStatus.Disputed,
        };

        foreach (var status in all)
            Assert.NotEmpty(OrderStateMachine.AllowedNext(status));

        foreach (var status in terminal)
            Assert.Empty(OrderStateMachine.AllowedNext(status));
    }

    [Fact]
    public void AllowedNext_IsConsistentWithValidateTransition()
    {
        // Every code AllowedNext reports as valid must pass ValidateTransition, and a code
        // it omits must fail — so the contract field the frontend consumes never disagrees
        // with the enforcement path.
        var allowed = OrderStateMachine.AllowedNext(OrderStatus.Ready);

        foreach (var to in allowed)
            OrderStateMachine.ValidateTransition(OrderStatus.Ready, to); // must not throw

        Assert.Throws<BusinessRuleException>(
            () => OrderStateMachine.ValidateTransition(OrderStatus.Ready, OrderStatus.Delivered));
    }
}
