using FluentValidation;
using laundryghar.Orders.Application.Delivery.Commands;
using laundryghar.Orders.Application.Delivery.Dtos;
using laundryghar.Orders.Application.Orders.Commands;
using laundryghar.Orders.Application.Orders.Dtos;
using laundryghar.Orders.Application.Pickup.Commands;
using laundryghar.Orders.Application.Pickup.Dtos;
using laundryghar.SharedDataModel.Enums;

namespace laundryghar.Orders.Tests.Validators;

/// <summary>
/// Validator unit tests for newly added order/pickup/delivery validators.
///
/// Covers:
///   - UpdateOrderStatusValidator (status enum whitelist)
///   - CancelOrderValidator / CancelOrderByCustomerValidator (reason length cap)
///   - UpdateDeliveryAssignmentValidator (delivery_assignments.status CHECK mirror)
///   - UpdateDeliverySlotValidator (slot capacity > 0, status whitelist)
///   - CreatePickupRequestAdminValidator / AssignPickupValidator
/// </summary>
public sealed class OrderValidatorTests
{
    // ────────────────────────────────────────────────────────────────────────────
    // UpdateOrderStatusValidator
    // ────────────────────────────────────────────────────────────────────────────

    private static UpdateOrderStatusCommand StatusCmd(string toStatus, string? reason = null) =>
        new(Guid.NewGuid(), new UpdateOrderStatusRequest(toStatus, reason, null, false), null);

    private readonly UpdateOrderStatusValidator _statusValidator = new();

    [Theory]
    [InlineData("placed")]
    [InlineData("pickup_scheduled")]
    [InlineData("pickup_assigned")]
    [InlineData("picked_up")]
    [InlineData("received")]
    [InlineData("sorting")]
    [InlineData("in_process")]
    [InlineData("qc")]
    [InlineData("ready")]
    [InlineData("delivery_scheduled")]
    [InlineData("delivery_assigned")]
    [InlineData("out_for_delivery")]
    [InlineData("delivered")]
    [InlineData("cancelled")]
    [InlineData("returned")]
    [InlineData("rewash")]
    [InlineData("disputed")]
    [InlineData("closed")]
    public void UpdateOrderStatus_ValidStatus_Passes(string status)
    {
        var result = _statusValidator.Validate(StatusCmd(status));
        Assert.True(result.IsValid, $"Expected '{status}' to pass.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("PLACED")]            // case-sensitive rejection (guard against casing bugs)
    [InlineData("unknown_status")]
    [InlineData("refunded")]          // not a valid order status
    public void UpdateOrderStatus_InvalidStatus_Fails(string status)
    {
        var result = _statusValidator.Validate(StatusCmd(status));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("ToStatus"));
    }

    [Fact]
    public void UpdateOrderStatus_ReasonExceeds500_Fails()
    {
        var longReason = new string('x', 501);
        var result = _statusValidator.Validate(StatusCmd("delivered", longReason));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void UpdateOrderStatus_ReasonExactly500_Passes()
    {
        var reason = new string('x', 500);
        var result = _statusValidator.Validate(StatusCmd("delivered", reason));
        Assert.True(result.IsValid);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // CancelOrderValidator
    // ────────────────────────────────────────────────────────────────────────────

    private readonly CancelOrderValidator _cancelValidator = new();

    [Fact]
    public void CancelOrder_NullReason_Passes()
    {
        var cmd = new CancelOrderCommand(Guid.NewGuid(), null, false, null);
        Assert.True(_cancelValidator.Validate(cmd).IsValid);
    }

    [Fact]
    public void CancelOrder_ReasonExceeds500_Fails()
    {
        var cmd = new CancelOrderCommand(Guid.NewGuid(), new string('x', 501), false, null);
        Assert.False(_cancelValidator.Validate(cmd).IsValid);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // CancelOrderByCustomerValidator
    // ────────────────────────────────────────────────────────────────────────────

    private readonly CancelOrderByCustomerValidator _cancelByCustomerValidator = new();

    [Fact]
    public void CancelOrderByCustomer_ValidCommand_Passes()
    {
        var cmd = new CancelOrderByCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "changed my mind");
        Assert.True(_cancelByCustomerValidator.Validate(cmd).IsValid);
    }

    [Fact]
    public void CancelOrderByCustomer_LongReason_Fails()
    {
        var cmd = new CancelOrderByCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('y', 501));
        Assert.False(_cancelByCustomerValidator.Validate(cmd).IsValid);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // UpdateDeliveryAssignmentValidator
    // ────────────────────────────────────────────────────────────────────────────

    private static UpdateDeliveryAssignmentCommand DelivCmd(string status) =>
        new(Guid.NewGuid(), new UpdateDeliveryAssignmentRequest(status), null);

    private readonly UpdateDeliveryAssignmentValidator _delivValidator = new();

    [Theory]
    [InlineData("assigned")]
    [InlineData("accepted")]
    [InlineData("started")]
    [InlineData("arrived")]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    public void UpdateDeliveryAssignment_ValidStatus_Passes(string status)
        => Assert.True(_delivValidator.Validate(DelivCmd(status)).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("pending")]
    [InlineData("delivered")]        // not a delivery_assignment status
    public void UpdateDeliveryAssignment_InvalidStatus_Fails(string status)
    {
        var result = _delivValidator.Validate(DelivCmd(status));
        Assert.False(result.IsValid);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // UpdateDeliverySlotValidator
    // ────────────────────────────────────────────────────────────────────────────

    private readonly UpdateDeliverySlotValidator _slotValidator = new();

    [Fact]
    public void UpdateDeliverySlot_ValidCapacity_Passes()
    {
        var cmd = new UpdateDeliverySlotCommand(Guid.NewGuid(), new UpdateDeliverySlotRequest(5, true, "active"), null);
        Assert.True(_slotValidator.Validate(cmd).IsValid);
    }

    [Fact]
    public void UpdateDeliverySlot_ZeroCapacity_Fails()
    {
        var cmd = new UpdateDeliverySlotCommand(Guid.NewGuid(), new UpdateDeliverySlotRequest(0, null, null), null);
        Assert.False(_slotValidator.Validate(cmd).IsValid);
    }

    [Fact]
    public void UpdateDeliverySlot_InvalidStatus_Fails()
    {
        var cmd = new UpdateDeliverySlotCommand(Guid.NewGuid(), new UpdateDeliverySlotRequest(null, null, "unknown"), null);
        Assert.False(_slotValidator.Validate(cmd).IsValid);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // AssignPickupValidator
    // ────────────────────────────────────────────────────────────────────────────

    private readonly AssignPickupValidator _assignPickupValidator = new();

    [Fact]
    public void AssignPickup_ValidRequest_Passes()
    {
        var cmd = new AssignPickupCommand(Guid.NewGuid(), new AssignPickupRequest(Guid.NewGuid()), null);
        Assert.True(_assignPickupValidator.Validate(cmd).IsValid);
    }

    [Fact]
    public void AssignPickup_EmptyRiderId_Fails()
    {
        var cmd = new AssignPickupCommand(Guid.NewGuid(), new AssignPickupRequest(Guid.Empty), null);
        Assert.False(_assignPickupValidator.Validate(cmd).IsValid);
    }
}
