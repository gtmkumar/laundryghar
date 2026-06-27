using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using operations.Application.Fulfillment;
using operations.Application.Fulfillment.Laundry;
using operations.Application.Fulfillment.Logistics;
using operations.Application.Fulfillment.Queries;
using operations.Application.Fulfillment.Salon;
using Xunit;

namespace operations.Tests.Fulfillment;

/// <summary>
/// Phase 4: the salon appointment strategy is the validation that the Phase-1 seam supports a
/// vertical with its OWN status vocabulary, resolved + described with no change to the shared spine.
/// </summary>
public class SalonStrategyTests
{
    private static readonly SalonAppointmentStrategy Salon = new();

    [Fact]
    public void Salon_happy_path_is_booked_through_completed()
        => Assert.Equal(
            new[] { SalonStatus.Booked, SalonStatus.Confirmed, SalonStatus.CheckedIn, SalonStatus.InService, SalonStatus.Completed },
            Salon.GetHappyPath());

    [Fact]
    public void Salon_mode_and_initial_are_appointment_booked()
    {
        Assert.Equal(FulfillmentMode.Appointment, Salon.FulfillmentMode);
        Assert.Equal(SalonStatus.Booked, Salon.InitialStatus);
    }

    [Theory]
    [InlineData(SalonStatus.Booked, SalonStatus.Confirmed)]
    [InlineData(SalonStatus.Confirmed, SalonStatus.CheckedIn)]
    [InlineData(SalonStatus.CheckedIn, SalonStatus.InService)]
    [InlineData(SalonStatus.InService, SalonStatus.Completed)]
    [InlineData(SalonStatus.Confirmed, SalonStatus.NoShow)]
    public void Salon_allows_legal_transitions(string from, string to) => Salon.EnsureTransition(from, to);

    [Theory]
    [InlineData(SalonStatus.Booked, SalonStatus.Completed)]   // can't skip to completed
    [InlineData(SalonStatus.InService, SalonStatus.Cancelled)] // can't cancel mid-service
    [InlineData(SalonStatus.Completed, SalonStatus.Booked)]    // terminal
    public void Salon_rejects_illegal_transitions(string from, string to)
        => Assert.Throws<BusinessRuleException>(() => Salon.EnsureTransition(from, to));

    [Fact]
    public void Salon_has_no_pickup_or_delivery_legs()
    {
        var legs = Salon.ResolveLegs(requestedPickup: true, requestedDelivery: true);
        Assert.False(legs.RequiresPickup);
        Assert.False(legs.RequiresDelivery);
        Assert.False(Salon.RequiresStoreDrop);
    }

    [Theory]
    [InlineData(SalonStatus.Booked, OrderLifecycleState.Created)]
    [InlineData(SalonStatus.Confirmed, OrderLifecycleState.Active)]
    [InlineData(SalonStatus.InService, OrderLifecycleState.Active)]
    [InlineData(SalonStatus.Completed, OrderLifecycleState.Completed)]
    [InlineData(SalonStatus.Cancelled, OrderLifecycleState.Cancelled)]
    [InlineData(SalonStatus.NoShow, OrderLifecycleState.Cancelled)]
    public void Salon_maps_its_private_vocabulary_to_neutral_super_states(string status, string expected)
        => Assert.Equal(expected, Salon.LifecycleStateFor(status));

    [Fact]
    public void Salon_initial_maps_to_created_and_every_status_maps_to_a_valid_super_state()
    {
        Assert.Equal(OrderLifecycleState.Created, Salon.LifecycleStateFor(Salon.InitialStatus));
        foreach (var status in Salon.GetTransitions().Keys)
            Assert.Contains(Salon.LifecycleStateFor(status), OrderLifecycleState.All);
    }

    [Fact]
    public void Salon_terminal_statuses_agree_with_terminal_lifecycle_states()
    {
        foreach (var status in Salon.GetTransitions().Keys)
            Assert.Equal(
                Salon.TerminalStatuses.Contains(status),
                OrderLifecycleState.Terminal.Contains(Salon.LifecycleStateFor(status)));
    }

    [Fact]
    public void Salon_customer_cancel_only_before_service()
    {
        Assert.True(Salon.CanCustomerCancel(SalonStatus.Booked));
        Assert.True(Salon.CanCustomerCancel(SalonStatus.Confirmed));
        Assert.False(Salon.CanCustomerCancel(SalonStatus.InService));
    }

    [Fact]
    public void Resolver_routes_appointment_mode_to_the_salon_strategy()
    {
        var resolver = new FulfillmentStrategyResolver(
            new IFulfillmentStrategy[] { new LaundryProcessStrategy(), new LogisticsPointToPointStrategy(), Salon });
        Assert.Same(Salon, resolver.Resolve(FulfillmentMode.Appointment));
    }

    // ── Phase 3: the fulfilment-config surface clients consume ──────────────────────────────

    [Fact]
    public async Task FulfillmentConfig_describes_every_registered_mode_including_salon()
    {
        var handler = new GetFulfillmentConfigHandler(
            new IFulfillmentStrategy[] { new LaundryProcessStrategy(), new LogisticsPointToPointStrategy(), Salon });

        var all = await handler.HandleAsync(new GetFulfillmentConfigQuery(), default);
        Assert.Equal(3, all.Count);

        var salon = await handler.HandleAsync(new GetFulfillmentConfigQuery(FulfillmentMode.Appointment), default);
        var cfg = Assert.Single(salon);
        Assert.Equal(SalonStatus.Booked, cfg.InitialStatus);
        Assert.False(cfg.RequiresPickup);
        Assert.Equal(SalonStatus.Booked, cfg.Stages[0].Status);
        Assert.Equal("Booked", cfg.Stages[0].Label);
        Assert.Equal("In Service", cfg.Stages[3].Label);
        Assert.Equal(OrderLifecycleState.Completed, cfg.Stages[^1].LifecycleState);
    }
}
