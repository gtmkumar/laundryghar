using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using operations.Application.Fulfillment;
using operations.Application.Fulfillment.Laundry;
using operations.Application.Fulfillment.Logistics;
using Xunit;

namespace operations.Tests.Fulfillment;

/// <summary>
/// Phase-1 gate: the exact-parity regression suite for the fulfilment state-machine extraction.
/// The expected transition graphs / happy paths below are the canonical originals from the former
/// <c>OrderStateMachine</c> (PRODUCTION_SPEC §4.1), encoded INDEPENDENTLY here so any future drift in
/// a strategy fails the build. This is what makes the upcoming destructive Phase-1 work safe.
/// </summary>
public class FulfillmentStrategyParityTests
{
    private static readonly LaundryProcessStrategy Laundry = new();
    private static readonly LogisticsPointToPointStrategy Logistics = new();

    // ── Canonical baselines (verbatim from the original static state machine) ──────────────

    private static readonly Dictionary<string, string[]> ExpectedLaundry = new()
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
        [OrderStatus.Cancelled]         = [],
        [OrderStatus.Closed]            = [],
    };

    private static readonly Dictionary<string, string[]> ExpectedParcel = new()
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
        [OrderStatus.Cancelled]         = [],
        [OrderStatus.Closed]            = [],
    };

    private static readonly string[] ExpectedLaundryHappyPath =
    [
        OrderStatus.Placed, OrderStatus.PickupScheduled, OrderStatus.PickupAssigned,
        OrderStatus.PickupInProgress, OrderStatus.PickedUp, OrderStatus.Received,
        OrderStatus.Sorting, OrderStatus.InProcess, OrderStatus.Qc, OrderStatus.Ready,
        OrderStatus.DeliveryScheduled, OrderStatus.DeliveryAssigned, OrderStatus.OutForDelivery,
        OrderStatus.Delivered, OrderStatus.Closed,
    ];

    private static readonly string[] ExpectedParcelHappyPath =
    [
        OrderStatus.Placed, OrderStatus.PickupScheduled, OrderStatus.PickupAssigned,
        OrderStatus.PickupInProgress, OrderStatus.PickedUp, OrderStatus.OutForDelivery,
        OrderStatus.Delivered, OrderStatus.Closed,
    ];

    // ── Transition-graph parity ────────────────────────────────────────────────────────────

    [Fact]
    public void Laundry_transitions_match_canonical_baseline()
        => AssertTransitionsMatch(ExpectedLaundry, Laundry);

    [Fact]
    public void Logistics_transitions_match_canonical_baseline()
        => AssertTransitionsMatch(ExpectedParcel, Logistics);

    private static void AssertTransitionsMatch(Dictionary<string, string[]> expected, IFulfillmentStrategy strategy)
    {
        var actual = strategy.GetTransitions();
        Assert.Equal(expected.Count, actual.Count);
        foreach (var (from, tos) in expected)
        {
            Assert.True(actual.ContainsKey(from), $"missing source status '{from}'");
            Assert.Equal(tos.OrderBy(x => x), actual[from].OrderBy(x => x));
        }
    }

    [Fact]
    public void Happy_paths_match_canonical_baseline()
    {
        Assert.Equal(ExpectedLaundryHappyPath, Laundry.GetHappyPath());
        Assert.Equal(ExpectedParcelHappyPath, Logistics.GetHappyPath());
    }

    // ── EnsureTransition (throwing parity with the old ValidateTransition) ───────────────────

    [Theory]
    [InlineData(OrderStatus.Qc, OrderStatus.Ready)]      // legal laundry
    [InlineData(OrderStatus.Placed, OrderStatus.Cancelled)]
    public void Laundry_EnsureTransition_allows_legal(string from, string to)
        => Laundry.EnsureTransition(from, to); // no throw

    [Theory]
    [InlineData(OrderStatus.PickedUp, OrderStatus.OutForDelivery)] // laundry must go through Received first
    [InlineData(OrderStatus.Placed, OrderStatus.Delivered)]
    public void Laundry_EnsureTransition_throws_on_illegal(string from, string to)
        => Assert.Throws<BusinessRuleException>(() => Laundry.EnsureTransition(from, to));

    [Fact]
    public void Logistics_skips_laundry_intake_states()
    {
        // Parcel: picked_up → out_for_delivery is legal (no received/sorting/qc).
        Logistics.EnsureTransition(OrderStatus.PickedUp, OrderStatus.OutForDelivery);
        Assert.Throws<BusinessRuleException>(() => Logistics.EnsureTransition(OrderStatus.PickedUp, OrderStatus.Received));
    }

    [Fact]
    public void EnsureTransition_throws_on_unknown_source()
        => Assert.Throws<BusinessRuleException>(() => Laundry.EnsureTransition("not_a_status", OrderStatus.Closed));

    // ── ForwardPath ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Laundry_ForwardPath_walks_intermediate_hops()
    {
        var hops = Laundry.ForwardPath(OrderStatus.PickupInProgress, OrderStatus.Received);
        Assert.Equal([OrderStatus.PickedUp, OrderStatus.Received], hops);
    }

    [Fact]
    public void Parcel_ForwardPath_skips_intake()
    {
        var hops = Logistics.ForwardPath(OrderStatus.PickedUp, OrderStatus.Delivered);
        Assert.Equal([OrderStatus.OutForDelivery, OrderStatus.Delivered], hops);
    }

    [Fact]
    public void ForwardPath_returns_empty_when_at_or_beyond_target()
    {
        Assert.Empty(Laundry.ForwardPath(OrderStatus.Delivered, OrderStatus.Placed));
        Assert.Empty(Laundry.ForwardPath(OrderStatus.Ready, OrderStatus.Ready));
    }

    // ── CanCustomerCancel ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrderStatus.Placed, true)]
    [InlineData(OrderStatus.PickupScheduled, true)]
    [InlineData(OrderStatus.PickedUp, false)]
    [InlineData(OrderStatus.InProcess, false)]
    public void CanCustomerCancel_matches_legacy_rule(string status, bool expected)
        => Assert.Equal(expected, Laundry.CanCustomerCancel(status));

    // ── Initial / terminal ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Initial_and_terminal_statuses_are_correct()
    {
        Assert.Equal(OrderStatus.Placed, Laundry.InitialStatus);
        Assert.Contains(OrderStatus.Delivered, Laundry.TerminalStatuses);
        Assert.Contains(OrderStatus.Closed, Logistics.TerminalStatuses);
    }

    // ── Resolver routing ─────────────────────────────────────────────────────────────────

    private static FulfillmentStrategyResolver NewResolver()
        => new(new IFulfillmentStrategy[] { Laundry, Logistics });

    [Fact]
    public void Resolver_routes_by_explicit_mode()
    {
        var r = NewResolver();
        Assert.Same(Laundry, r.Resolve(FulfillmentMode.ProcessDeliver));
        Assert.Same(Logistics, r.Resolve(FulfillmentMode.PointToPoint));
    }

    [Fact]
    public void Resolver_falls_back_to_laundry_for_unknown_or_null()
    {
        var r = NewResolver();
        Assert.Same(Laundry, r.Resolve(null));
        Assert.Same(Laundry, r.Resolve("appointment")); // not yet registered → fallback
    }

    [Fact]
    public void ResolveForOrder_prefers_stored_mode_then_legacy_jobtype()
    {
        var r = NewResolver();
        Assert.Same(Logistics, r.ResolveForOrder(new Order { FulfillmentMode = FulfillmentMode.PointToPoint, JobType = JobType.Laundry }));
        Assert.Same(Laundry,   r.ResolveForOrder(new Order { FulfillmentMode = FulfillmentMode.ProcessDeliver, JobType = JobType.Parcel }));
        // un-backfilled row (empty mode) → derive from legacy JobType
        Assert.Same(Logistics, r.ResolveForOrder(new Order { FulfillmentMode = "", JobType = JobType.Parcel }));
        Assert.Same(Laundry,   r.ResolveForOrder(new Order { FulfillmentMode = "", JobType = JobType.Laundry }));
    }
}
