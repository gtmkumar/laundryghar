using laundryghar.Orders.Infrastructure.Auth;
using laundryghar.Orders.Infrastructure.Services;
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
    /// Parcel (point-to-point) transitions — same spine as laundry but skips the
    /// laundry-only intake/processing states (received → sorting → in_process → qc →
    /// ready) and the separate delivery-assignment leg. A parcel is a single A→B trip:
    ///   placed → pickup_scheduled → pickup_assigned → pickup_in_progress → picked_up
    ///          → out_for_delivery → delivered → closed
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> ParcelTransitions = new()
    {
        [OrderStatus.Placed]            = [OrderStatus.PickupScheduled, OrderStatus.Cancelled, OrderStatus.Disputed],
        [OrderStatus.PickupScheduled]   = [OrderStatus.PickupAssigned, OrderStatus.Cancelled, OrderStatus.Disputed],
        [OrderStatus.PickupAssigned]    = [OrderStatus.PickupInProgress, OrderStatus.Cancelled, OrderStatus.Disputed],
        [OrderStatus.PickupInProgress]  = [OrderStatus.PickedUp, OrderStatus.Cancelled, OrderStatus.Disputed],
        [OrderStatus.PickedUp]          = [OrderStatus.OutForDelivery, OrderStatus.Disputed],
        [OrderStatus.OutForDelivery]    = [OrderStatus.Delivered, OrderStatus.Returned, OrderStatus.Disputed],
        [OrderStatus.Delivered]         = [OrderStatus.Closed, OrderStatus.Disputed],
        [OrderStatus.Returned]          = [OrderStatus.Closed],
        [OrderStatus.Disputed]          = [OrderStatus.Closed],
        [OrderStatus.Cancelled]         = [],   // terminal
        [OrderStatus.Closed]            = [],   // terminal
    };

    private static Dictionary<string, HashSet<string>> MapFor(string jobType)
        => jobType == JobType.Parcel ? ParcelTransitions : AllowedTransitions;

    /// <summary>
    /// Returns true if the transition from → to is valid for the given job type.
    /// Throws BusinessRuleException with a descriptive message if invalid.
    /// jobType defaults to laundry so existing callers are unaffected.
    /// </summary>
    public static void ValidateTransition(string from, string to, string jobType = JobType.Laundry)
    {
        var map = MapFor(jobType);
        if (!map.TryGetValue(from, out var allowed))
            throw new BusinessRuleException($"Unknown source status '{from}' for job type '{jobType}'.");

        if (!allowed.Contains(to))
            throw new BusinessRuleException(
                $"Invalid status transition: '{from}' → '{to}' (job type '{jobType}'). " +
                $"Allowed targets: [{string.Join(", ", allowed)}].");
    }

    /// <summary>Returns all valid next statuses from the given status for the job type.</summary>
    public static IReadOnlySet<string> AllowedNext(string from, string jobType = JobType.Laundry)
        => MapFor(jobType).TryGetValue(from, out var set) ? set : new HashSet<string>();

    /// <summary>Returns true if the order can be customer-cancelled from the given status.</summary>
    public static bool CanCustomerCancel(string status) =>
        status is OrderStatus.Placed or OrderStatus.PickupScheduled;

    /// <summary>
    /// The single linear "happy path" the pickup→intake flow advances along. Used by
    /// <see cref="ForwardPath"/> to walk an order that has fallen behind reality (e.g.
    /// a rider completed the physical pickup but the order was still pickup_scheduled).
    /// </summary>
    private static readonly string[] HappyPath =
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

    /// <summary>Parcel linear happy path — skips laundry intake/processing + delivery-leg states.</summary>
    private static readonly string[] ParcelHappyPath =
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

    /// <summary>
    /// DEFECT 6 — returns the ordered list of statuses to step THROUGH to advance an
    /// order from <paramref name="from"/> up to <paramref name="target"/> along the
    /// linear happy path (excludes <paramref name="from"/>, includes
    /// <paramref name="target"/>). Each hop is a legal single-step transition, so
    /// callers can record one status-history row per hop. Returns an empty list when
    /// the order is already at or beyond the target, or when either status is off the
    /// linear path (caller should then no-op rather than force an illegal jump).
    /// </summary>
    public static IReadOnlyList<string> ForwardPath(string from, string target, string jobType = JobType.Laundry)
    {
        var path = jobType == JobType.Parcel ? ParcelHappyPath : HappyPath;
        var fromIdx = Array.IndexOf(path, from);
        var targetIdx = Array.IndexOf(path, target);
        if (fromIdx < 0 || targetIdx < 0 || targetIdx <= fromIdx)
            return [];

        var hops = new List<string>(targetIdx - fromIdx);
        for (var i = fromIdx + 1; i <= targetIdx; i++)
            hops.Add(path[i]);
        return hops;
    }
}
