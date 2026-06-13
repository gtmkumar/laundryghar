using laundryghar.Orders.Application.Common;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;

namespace laundryghar.Orders.Tests.Ops;

/// <summary>
/// Parcel (point-to-point) job type rides the same order spine as laundry but follows
/// a shorter state path: it skips the laundry intake/processing states
/// (received → sorting → in_process → qc → ready) and the separate delivery-assignment
/// leg, going picked_up → out_for_delivery directly. These tests pin that contract and
/// confirm laundry behaviour is unchanged.
/// </summary>
public sealed class ParcelStateMachineTests
{
    [Fact]
    public void Parcel_PickedUp_GoesStraightToOutForDelivery()
    {
        var next = OrderStateMachine.AllowedNext(OrderStatus.PickedUp, JobType.Parcel);
        Assert.Contains(OrderStatus.OutForDelivery, next);
        Assert.DoesNotContain(OrderStatus.Received, next);
    }

    [Fact]
    public void Laundry_PickedUp_StillGoesToReceived()
    {
        // Backward-compat: laundry (default) keeps the intake step.
        var next = OrderStateMachine.AllowedNext(OrderStatus.PickedUp);
        Assert.Contains(OrderStatus.Received, next);
        Assert.DoesNotContain(OrderStatus.OutForDelivery, next);
    }

    [Fact]
    public void Parcel_LaundryIntakeStates_AreNotReachable()
    {
        // received/sorting/in_process/qc/ready are unknown sources on the parcel map.
        foreach (var s in new[] { OrderStatus.Received, OrderStatus.Sorting,
                                  OrderStatus.InProcess, OrderStatus.Qc, OrderStatus.Ready })
            Assert.Empty(OrderStateMachine.AllowedNext(s, JobType.Parcel));
    }

    [Fact]
    public void Parcel_RejectsLaundryOnlyTransition()
    {
        Assert.Throws<BusinessRuleException>(() =>
            OrderStateMachine.ValidateTransition(
                OrderStatus.PickedUp, OrderStatus.Received, JobType.Parcel));
    }

    [Fact]
    public void Parcel_AllowsFullHappyPath()
    {
        // Walk the documented parcel happy path; each hop must validate.
        string[] path =
        {
            OrderStatus.Placed, OrderStatus.PickupScheduled, OrderStatus.PickupAssigned,
            OrderStatus.PickupInProgress, OrderStatus.PickedUp, OrderStatus.OutForDelivery,
            OrderStatus.Delivered, OrderStatus.Closed,
        };

        for (var i = 0; i < path.Length - 1; i++)
            OrderStateMachine.ValidateTransition(path[i], path[i + 1], JobType.Parcel); // must not throw
    }

    [Fact]
    public void Parcel_ForwardPath_SkipsLaundryStates()
    {
        var hops = OrderStateMachine.ForwardPath(
            OrderStatus.PickupInProgress, OrderStatus.Delivered, JobType.Parcel);

        Assert.Equal(
            new[] { OrderStatus.PickedUp, OrderStatus.OutForDelivery, OrderStatus.Delivered },
            hops);
    }

    [Fact]
    public void Parcel_CanCancelEarly_NotAfterPickup()
    {
        Assert.Contains(OrderStatus.Cancelled,
            OrderStateMachine.AllowedNext(OrderStatus.PickupAssigned, JobType.Parcel));
        Assert.DoesNotContain(OrderStatus.Cancelled,
            OrderStateMachine.AllowedNext(OrderStatus.PickedUp, JobType.Parcel));
    }
}
