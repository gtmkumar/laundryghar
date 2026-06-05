namespace laundryghar.Orders.Application.Common;

/// <summary>
/// Order status state machine — enforces valid transitions per PRODUCTION_SPEC §4.1.
///
/// Valid forward transitions (happy path):
///   placed → pickup_scheduled → pickup_assigned → pickup_in_progress → picked_up
///          → received → sorting → in_process → qc → ready
///          → delivery_scheduled → delivery_assigned → out_for_delivery → delivered → closed
///
/// Branch transitions:
///   placed / pickup_scheduled / pickup_assigned → cancelled
///   ready / delivery_scheduled / delivery_assigned / out_for_delivery → returned
///   any active status → disputed
///   delivered / returned → rewash (initiates a new wash cycle)
///   closed is terminal (no transitions out except for admin override — not in scope)
///
/// Cancel is handled by a dedicated endpoint but uses the same state check.
/// </summary>
public static class OrderStateMachine
{
    private static readonly Dictionary<string, HashSet<string>> AllowedTransitions = new()
    {
        [OrderStatus.Placed]            = [OrderStatus.PickupScheduled, OrderStatus.Cancelled, OrderStatus.Disputed],
        [OrderStatus.PickupScheduled]   = [OrderStatus.PickupAssigned, OrderStatus.Cancelled, OrderStatus.Disputed],
        [OrderStatus.PickupAssigned]    = [OrderStatus.PickupInProgress, OrderStatus.Cancelled, OrderStatus.Disputed],
        [OrderStatus.PickupInProgress]  = [OrderStatus.PickedUp, OrderStatus.Cancelled, OrderStatus.Disputed],
        [OrderStatus.PickedUp]          = [OrderStatus.Received, OrderStatus.Disputed],
        [OrderStatus.Received]          = [OrderStatus.Sorting, OrderStatus.Disputed],
        [OrderStatus.Sorting]           = [OrderStatus.InProcess, OrderStatus.Disputed],
        [OrderStatus.InProcess]         = [OrderStatus.Qc, OrderStatus.Disputed],
        [OrderStatus.Qc]                = [OrderStatus.Ready, OrderStatus.Rewash, OrderStatus.Disputed],
        [OrderStatus.Ready]             = [OrderStatus.DeliveryScheduled, OrderStatus.Returned, OrderStatus.Disputed],
        [OrderStatus.DeliveryScheduled] = [OrderStatus.DeliveryAssigned, OrderStatus.Returned, OrderStatus.Disputed],
        [OrderStatus.DeliveryAssigned]  = [OrderStatus.OutForDelivery, OrderStatus.Returned, OrderStatus.Disputed],
        [OrderStatus.OutForDelivery]    = [OrderStatus.Delivered, OrderStatus.Returned, OrderStatus.Disputed],
        [OrderStatus.Delivered]         = [OrderStatus.Closed, OrderStatus.Rewash, OrderStatus.Disputed],
        [OrderStatus.Rewash]            = [OrderStatus.Sorting, OrderStatus.Disputed],
        [OrderStatus.Returned]          = [OrderStatus.Closed],
        [OrderStatus.Disputed]          = [OrderStatus.Closed, OrderStatus.InProcess],
        [OrderStatus.Cancelled]         = [],   // terminal
        [OrderStatus.Closed]            = [],   // terminal
    };

    /// <summary>
    /// Returns true if the transition from → to is valid.
    /// Throws BusinessRuleException with a descriptive message if invalid.
    /// </summary>
    public static void ValidateTransition(string from, string to)
    {
        if (!AllowedTransitions.TryGetValue(from, out var allowed))
            throw new BusinessRuleException($"Unknown source status '{from}'.");

        if (!allowed.Contains(to))
            throw new BusinessRuleException(
                $"Invalid status transition: '{from}' → '{to}'. " +
                $"Allowed targets: [{string.Join(", ", allowed)}].");
    }

    /// <summary>Returns all valid next statuses from the given status.</summary>
    public static IReadOnlySet<string> AllowedNext(string from)
        => AllowedTransitions.TryGetValue(from, out var set) ? set : new HashSet<string>();

    /// <summary>Returns true if the order can be customer-cancelled from the given status.</summary>
    public static bool CanCustomerCancel(string status) =>
        status is OrderStatus.Placed or OrderStatus.PickupScheduled;
}
