namespace operations.Application.Fulfillment;

/// <summary>
/// Per-vertical fulfilment behaviour, resolved by <c>order.VerticalKey</c> via
/// <see cref="IFulfillmentStrategyResolver"/>. This is the seam the multi-vertical
/// migration is built on — the laundry wash/QC pipeline, the logistics point-to-point
/// trip, and (later) the salon appointment lifecycle each become one implementation.
///
/// <para><b>Phase 0 scope:</b> this contract is intentionally limited to the order
/// state-machine surface (the well-defined, self-contained part of the seam). It is
/// registered in DI but NOT yet consumed by the live order path — the Phase 1 work
/// (widening <c>OrderStatus</c>, severing <c>Order.Garments</c>) routes callers through
/// the resolver. Order-creation/dispatch/tax/catalog hooks are added in later phases as
/// their supporting value types land. See <c>docs/MULTI_VERTICAL_BLUEPRINT.md</c> §2.2.</para>
/// </summary>
public interface IFulfillmentStrategy
{
    /// <summary>The vertical this strategy serves — see <c>SharedDataModel.Enums.VerticalKey</c>.</summary>
    string VerticalKey { get; }

    /// <summary>The status a freshly created order of this vertical starts in.</summary>
    string InitialStatus { get; }

    /// <summary>Statuses from which no further transition is allowed.</summary>
    IReadOnlySet<string> TerminalStatuses { get; }

    /// <summary>The full transition graph: status → its allowed next statuses.</summary>
    IReadOnlyDictionary<string, IReadOnlySet<string>> GetTransitions();

    /// <summary>The single linear happy path the fulfilment flow advances along.</summary>
    IReadOnlyList<string> GetHappyPath();

    /// <summary>True if <paramref name="status"/> is part of this vertical's status vocabulary.</summary>
    bool IsKnownStatus(string status);

    /// <summary>
    /// True if the <paramref name="from"/> → <paramref name="to"/> transition is legal.
    /// Non-throwing (unlike the legacy static state machine) so it composes cleanly.
    /// </summary>
    bool ValidateTransition(string from, string to);

    /// <summary>
    /// The ordered statuses to step THROUGH to advance from <paramref name="from"/> up to
    /// <paramref name="target"/> along the linear happy path (excludes <paramref name="from"/>,
    /// includes <paramref name="target"/>); empty when already at/beyond target or off-path.
    /// </summary>
    IReadOnlyList<string> ForwardPath(string from, string target);
}
