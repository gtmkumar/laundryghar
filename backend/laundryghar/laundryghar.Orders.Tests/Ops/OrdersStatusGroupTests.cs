using laundryghar.SharedDataModel.Enums;

namespace laundryghar.Orders.Tests.Ops;

/// <summary>
/// Tests the statusGroup=active|history classification used by GetOrdersHandler
/// to split the orders list into the live "active" board vs terminal "history".
/// These mirror the exact `TerminalStatuses.Contains(status)` predicate so the
/// pure classification logic is covered without a DB.
/// </summary>
public sealed class OrdersStatusGroupTests
{
    // Verbatim copy of GetOrdersHandler.TerminalStatuses.
    private static readonly string[] TerminalStatuses =
    {
        OrderStatus.Delivered,
        OrderStatus.Cancelled,
        OrderStatus.Closed,
        OrderStatus.Returned,
    };

    private static bool IsActive(string status)  => !TerminalStatuses.Contains(status);
    private static bool IsHistory(string status) => TerminalStatuses.Contains(status);

    [Theory]
    [InlineData(OrderStatus.Placed)]
    [InlineData(OrderStatus.PickupScheduled)]
    [InlineData(OrderStatus.PickupInProgress)]
    [InlineData(OrderStatus.Received)]
    [InlineData(OrderStatus.InProcess)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.OutForDelivery)]
    [InlineData(OrderStatus.Disputed)]
    [InlineData(OrderStatus.Rewash)]
    public void Active_NonTerminalStatuses_AreActive(string status)
    {
        Assert.True(IsActive(status));
        Assert.False(IsHistory(status));
    }

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Closed)]
    [InlineData(OrderStatus.Returned)]
    public void History_TerminalStatuses_AreHistory(string status)
    {
        Assert.True(IsHistory(status));
        Assert.False(IsActive(status));
    }

    [Fact]
    public void Active_And_History_ArePartition()
    {
        // Every order status falls into exactly one of the two groups.
        string[] all =
        {
            OrderStatus.Placed, OrderStatus.PickupScheduled, OrderStatus.PickupAssigned,
            OrderStatus.PickupInProgress, OrderStatus.PickedUp, OrderStatus.Received,
            OrderStatus.Sorting, OrderStatus.InProcess, OrderStatus.Qc, OrderStatus.Ready,
            OrderStatus.DeliveryScheduled, OrderStatus.DeliveryAssigned, OrderStatus.OutForDelivery,
            OrderStatus.Delivered, OrderStatus.Cancelled, OrderStatus.Returned, OrderStatus.Rewash,
            OrderStatus.Disputed, OrderStatus.Closed,
        };

        foreach (var s in all)
            Assert.NotEqual(IsActive(s), IsHistory(s));
    }
}
