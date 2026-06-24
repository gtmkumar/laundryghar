namespace operations.Application.Fulfillment;

/// <summary>
/// Per-fulfilment-mode order behaviour, resolved by <c>order.FulfillmentMode</c> via
/// <see cref="IFulfillmentStrategyResolver"/>. This is the seam the multi-vertical
/// migration is built on: the laundry wash/QC pipeline (<c>process_deliver</c>), the
/// logistics point-to-point trip (<c>point_to_point</c>), and (later) the salon
/// appointment lifecycle (<c>appointment</c>) each become one implementation.
///
/// <para><b>Keyed by FulfillmentMode, not VerticalKey.</b> Evidence from the live data:
/// a single laundry-vertical brand legitimately runs both laundry and parcel jobs, so the
/// state-machine/leg-topology discriminator is the per-order <c>FulfillmentMode</c>, while
/// <c>VerticalKey</c> stays the brand-level catalog/branding/entitlement discriminator.
/// This resolves the JobType↔VerticalKey orthogonality the blueprint left open (§8 OQ).</para>
///
/// <para><b>Phase 1 scope:</b> this contract owns the order state machine (extracted from
/// the former <c>OrderStateMachine</c> static). Order-creation/dispatch/tax/catalog hooks
/// are added in later phases as their supporting value types land — see
/// <c>docs/MULTI_VERTICAL_BLUEPRINT.md</c> §2.2.</para>
/// </summary>
public interface IFulfillmentStrategy
{
    /// <summary>The fulfilment mode this strategy serves — see <c>SharedDataModel.Enums.FulfillmentMode</c>.</summary>
    string FulfillmentMode { get; }

    /// <summary>The status a freshly created order in this mode starts in.</summary>
    string InitialStatus { get; }

    /// <summary>Statuses from which no further transition is allowed.</summary>
    IReadOnlySet<string> TerminalStatuses { get; }

    /// <summary>The full transition graph: status → its allowed next statuses.</summary>
    IReadOnlyDictionary<string, IReadOnlySet<string>> GetTransitions();

    /// <summary>The single linear happy path the fulfilment flow advances along.</summary>
    IReadOnlyList<string> GetHappyPath();

    /// <summary>True if <paramref name="status"/> is part of this mode's status vocabulary.</summary>
    bool IsKnownStatus(string status);

    /// <summary>The allowed next statuses from <paramref name="from"/> (empty if none/unknown).</summary>
    IReadOnlySet<string> AllowedNext(string from);

    /// <summary>Non-throwing predicate: is <paramref name="from"/> → <paramref name="to"/> legal?</summary>
    bool CanTransition(string from, string to);

    /// <summary>Throws <c>BusinessRuleException</c> if the transition is illegal (parity with the
    /// former <c>OrderStateMachine.ValidateTransition</c>).</summary>
    void EnsureTransition(string from, string to);

    /// <summary>
    /// The ordered statuses to step THROUGH to advance from <paramref name="from"/> up to
    /// <paramref name="target"/> along the linear happy path (excludes <paramref name="from"/>,
    /// includes <paramref name="target"/>); empty when already at/beyond target or off-path.
    /// </summary>
    IReadOnlyList<string> ForwardPath(string from, string target);

    /// <summary>True if the order can be customer-cancelled from <paramref name="status"/>.</summary>
    bool CanCustomerCancel(string status);

    /// <summary>
    /// The generic, vertical-neutral lifecycle super-state (<c>OrderLifecycleState.*</c>) that
    /// the given detailed sub-status maps to. The shared order spine persists this on
    /// <c>orders.lifecycle_state</c> so platform code reasons over a closed neutral vocabulary
    /// while each strategy owns its detailed <c>status</c>. See <see cref="StateMachineStrategyBase"/>
    /// for the default (shared <c>OrderStatus</c> vocabulary) mapping.
    /// </summary>
    string LifecycleStateFor(string status);

    // ── Behavioural hooks (Phase 1 delegation) ───────────────────────────────────────────

    /// <summary>
    /// The pickup/delivery legs a new order in this mode requires, given the caller's requested
    /// flags. <c>process_deliver</c> honours the request; <c>point_to_point</c> forces both
    /// (it is, by definition, an origin→destination trip). Used by order creation.
    /// </summary>
    FulfilmentLegs ResolveLegs(bool requestedPickup, bool requestedDelivery);

    /// <summary>
    /// The detailed status an order advances to once its pickup leg physically completes.
    /// <c>process_deliver</c> routes into intake (<c>received</c>); <c>point_to_point</c> skips
    /// intake and goes straight to <c>out_for_delivery</c>. Used by the rider pickup-leg flow.
    /// </summary>
    string PostPickupStatus { get; }

    /// <summary>
    /// Whether a pickup leg in this mode involves a store/warehouse drop (collect from customer →
    /// drop for processing). <c>process_deliver</c>: true; <c>point_to_point</c>: false (direct
    /// trip). Gates the geofence store-drop stamp.
    /// </summary>
    bool RequiresStoreDrop { get; }

    /// <summary>
    /// Stamp the lifecycle timestamp(s) for entering <paramref name="toStatus"/> on
    /// <paramref name="order"/> (e.g. <c>picked_up</c>→PickedUpAt, <c>ready</c>→Ready/QcCompleted).
    /// Centralizes the per-status side-effects so shared handlers don't hardcode laundry statuses.
    /// No-op for statuses with no associated timestamp.
    /// </summary>
    void ApplyTransitionEffects(laundryghar.SharedDataModel.Entities.OrderLifecycle.Order order, string toStatus, DateTimeOffset now);
}

/// <summary>The pickup/delivery legs a new order requires — see <see cref="IFulfillmentStrategy.ResolveLegs"/>.</summary>
public readonly record struct FulfilmentLegs(bool RequiresPickup, bool RequiresDelivery);
