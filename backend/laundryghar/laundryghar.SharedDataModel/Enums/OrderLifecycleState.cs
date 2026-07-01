namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Generic, vertical-neutral order lifecycle super-state, denormalized onto
/// <c>order_lifecycle.orders.lifecycle_state</c>. This is the multi-vertical spine's
/// status: the shared platform (reporting, queues, clients) reasons over these five
/// states without knowing any vertical's detailed sub-status (<c>orders.status</c>),
/// which is owned by the order's <c>IFulfillmentStrategy</c>.
///
/// <para>Phase 1 (blueprint §7) widens OrderStatus into "generic super-states +
/// strategy sub-status". The detailed <c>status</c> CHECK is relaxed (the strategy is
/// the source of truth for sub-status validity); this column carries the closed,
/// neutral super-state vocabulary instead.</para>
/// </summary>
public static class OrderLifecycleState
{
    /// <summary>Order created, fulfilment not yet started.</summary>
    public const string Created = "created";

    /// <summary>In fulfilment (any intermediate working/exception sub-status).</summary>
    public const string Active = "active";

    /// <summary>Fulfilment concluded with an outcome (delivered / returned).</summary>
    public const string Completed = "completed";

    /// <summary>Cancelled before completion.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Final wrap-up state (post-completion close, dispute resolution).</summary>
    public const string Closed = "closed";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Created, Active, Completed, Cancelled, Closed };

    /// <summary>Terminal super-states — no further fulfilment progress is expected. The
    /// complement (<see cref="Created"/> + <see cref="Active"/>) is the "open / in-flight"
    /// set ops queues work over. Vertical-neutral: replaces per-vertical terminal-status lists.</summary>
    public static readonly IReadOnlySet<string> Terminal =
        new HashSet<string> { Completed, Cancelled, Closed };

    /// <summary>Same terminal set as a plain array — EF Core can translate <c>array.Contains(col)</c>
    /// to SQL (<c>= ANY</c>), whereas <see cref="Terminal"/> (an <see cref="IReadOnlySet{T}"/>) is NOT
    /// translatable. Use this in LINQ-to-DB predicates; use <see cref="Terminal"/> for in-memory checks.</summary>
    public static readonly string[] TerminalArray = [Completed, Cancelled, Closed];

    public static bool IsValid(string? value) => value is not null && All.Contains(value);

    /// <summary>
    /// Maps a sub-status from the shared <see cref="OrderStatus"/> vocabulary (laundry +
    /// logistics) to its generic super-state. This is the default used by
    /// <c>StateMachineStrategyBase.LifecycleStateFor</c>; verticals with their own status
    /// vocabulary (e.g. salon) override the strategy hook instead of extending this switch.
    /// Must stay in lockstep with the backfill CASE in
    /// <c>db/patches/phase1_slice_b_order_lifecycle_state.sql</c>.
    /// </summary>
    public static string ForOrderStatus(string status) => status switch
    {
        OrderStatus.Placed                          => Created,
        OrderStatus.Cancelled                       => Cancelled,
        OrderStatus.Closed                          => Closed,
        OrderStatus.Delivered or OrderStatus.Returned => Completed,
        _                                           => Active,
    };
}
