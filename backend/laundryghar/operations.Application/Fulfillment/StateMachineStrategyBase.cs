using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;

namespace operations.Application.Fulfillment;

/// <summary>
/// Shared state-machine plumbing for fulfilment strategies. A concrete strategy supplies its
/// transition graph + happy path; this base implements the walk/validate/forward-path logic
/// (extracted verbatim from the former <c>OrderStateMachine</c> static so behaviour is preserved).
/// </summary>
public abstract class StateMachineStrategyBase : IFulfillmentStrategy
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();

    public abstract string FulfillmentMode { get; }
    public abstract string InitialStatus { get; }
    public abstract IReadOnlySet<string> TerminalStatuses { get; }

    /// <summary>status → allowed next statuses — the single source of truth for this mode.</summary>
    protected abstract IReadOnlyDictionary<string, IReadOnlySet<string>> Transitions { get; }

    /// <summary>The linear happy path used by <see cref="ForwardPath"/>.</summary>
    protected abstract IReadOnlyList<string> HappyPath { get; }

    public IReadOnlyDictionary<string, IReadOnlySet<string>> GetTransitions() => Transitions;
    public IReadOnlyList<string> GetHappyPath() => HappyPath;
    public bool IsKnownStatus(string status) => Transitions.ContainsKey(status);

    public IReadOnlySet<string> AllowedNext(string from)
        => Transitions.TryGetValue(from, out var set) ? set : Empty;

    public bool CanTransition(string from, string to) => AllowedNext(from).Contains(to);

    public void EnsureTransition(string from, string to)
    {
        if (!Transitions.TryGetValue(from, out var allowed))
            throw new BusinessRuleException(
                $"Unknown source status '{from}' for fulfilment mode '{FulfillmentMode}'.");

        if (!allowed.Contains(to))
            throw new BusinessRuleException(
                $"Invalid status transition: '{from}' → '{to}' (fulfilment mode '{FulfillmentMode}'). " +
                $"Allowed targets: [{string.Join(", ", allowed)}].");
    }

    public IReadOnlyList<string> ForwardPath(string from, string target)
    {
        int fromIdx = -1, targetIdx = -1;
        for (var i = 0; i < HappyPath.Count; i++)
        {
            if (HappyPath[i] == from) fromIdx = i;
            if (HappyPath[i] == target) targetIdx = i;
        }
        if (fromIdx < 0 || targetIdx < 0 || targetIdx <= fromIdx) return [];

        var hops = new List<string>(targetIdx - fromIdx);
        for (var i = fromIdx + 1; i <= targetIdx; i++) hops.Add(HappyPath[i]);
        return hops;
    }

    /// <summary>Generic across current modes (placed / pickup_scheduled); a mode may override.</summary>
    public virtual bool CanCustomerCancel(string status)
        => status is OrderStatus.Placed or OrderStatus.PickupScheduled;

    /// <summary>
    /// Default super-state mapping for strategies on the shared <see cref="OrderStatus"/>
    /// vocabulary (laundry <c>process_deliver</c> + logistics <c>point_to_point</c>): delegates
    /// to <see cref="OrderLifecycleState.ForOrderStatus"/>. A strategy with its own status
    /// vocabulary (e.g. a future salon appointment flow) overrides this.
    /// </summary>
    public virtual string LifecycleStateFor(string status)
        => OrderLifecycleState.ForOrderStatus(status);

    // ── Behavioural hooks ────────────────────────────────────────────────────────────────

    public abstract string PostPickupStatus { get; }
    public abstract bool RequiresStoreDrop { get; }

    /// <summary>Default: honour the caller's requested legs. Modes that mandate legs override.</summary>
    public virtual FulfilmentLegs ResolveLegs(bool requestedPickup, bool requestedDelivery)
        => new(requestedPickup, requestedDelivery);

    /// <summary>
    /// Shared status→timestamp side-effects for the <see cref="OrderStatus"/> vocabulary
    /// (extracted verbatim from the former inline switch in UpdateOrderStatus). Statuses with no
    /// timestamp (e.g. <c>qc</c>, <c>disputed</c>) are a no-op. Cancellation timestamps are owned
    /// by the cancel handlers. A mode with its own vocabulary overrides this.
    /// </summary>
    public virtual void ApplyTransitionEffects(
        laundryghar.SharedDataModel.Entities.OrderLifecycle.Order order, string toStatus, DateTimeOffset now)
    {
        switch (toStatus)
        {
            case OrderStatus.PickupScheduled: order.PickupScheduledAt = now; break;
            case OrderStatus.PickedUp:        order.PickedUpAt        = now; break;
            case OrderStatus.Received:        order.ReceivedAt        = now; break;
            case OrderStatus.Ready:           order.ReadyAt           = now;
                                              order.QcCompletedAt     = now; break;
            case OrderStatus.OutForDelivery:  order.OutForDeliveryAt  = now; break;
            case OrderStatus.Delivered:       order.DeliveredAt       = now; break;
        }
    }
}
