using laundryghar.Catalog.Application.Pricing.Queries;
using laundryghar.Logistics.Application.RiderSelf;
using laundryghar.Orders.Application.Common;
using laundryghar.Orders.Application.Pickup.Commands;
using laundryghar.Orders.Application.Pickup.Dtos;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.SharedDataModel.Enums;

namespace laundryghar.Orders.Tests.MobileQa;

/// <summary>
/// Pure unit tests for the mobile-QA backend defect fixes (D1–D4, D6, D7) that live
/// in the Operations assembly. DB-touching paths are exercised by the deferred live
/// E2E plan; these target the extracted decision/mapping logic.
/// </summary>
public sealed class MobileQaBackendDefectTests
{
    // ── DEFECT 1: price-list display label join ─────────────────────────────────

    [Fact]
    public void D1_BuildLabel_JoinsItemAndService()
    {
        Assert.Equal("Shirt · Wash & Iron",
            GetPublishedPriceListHandler.BuildLabel("Shirt", "Wash & Iron"));
    }

    [Fact]
    public void D1_BuildLabel_OmitsBlankParts()
    {
        Assert.Equal("Shirt", GetPublishedPriceListHandler.BuildLabel("Shirt", null));
        Assert.Equal("Wash & Iron", GetPublishedPriceListHandler.BuildLabel("  ", "Wash & Iron"));
    }

    [Fact]
    public void D1_BuildLabel_AllBlank_FallsBackToItem()
    {
        Assert.Equal("Item", GetPublishedPriceListHandler.BuildLabel(null, null));
    }

    // ── DEFECT 2: cart-item FK validation ───────────────────────────────────────

    private static RequestedCartItemDto Cart(Guid? itemId, Guid? serviceId)
        => new(serviceId, itemId, "Label", 1, null);

    [Fact]
    public void D2_AllValidIds_NoErrors()
    {
        var item = Guid.NewGuid();
        var svc = Guid.NewGuid();
        var errors = CustomerSchedulePickupHandler.BuildCartItemErrors(
            [Cart(item, svc)],
            new HashSet<Guid> { item },
            new HashSet<Guid> { svc });

        Assert.Empty(errors);
    }

    [Fact]
    public void D2_UnknownItemId_ReportsOffendingIndex()
    {
        var goodItem = Guid.NewGuid();
        var bogusItem = Guid.NewGuid(); // e.g. a price-list ROW id, not an item id

        var errors = CustomerSchedulePickupHandler.BuildCartItemErrors(
            [Cart(goodItem, null), Cart(bogusItem, null)],
            new HashSet<Guid> { goodItem },
            new HashSet<Guid>());

        Assert.Single(errors);
        Assert.Contains("cartItems[1].itemId", errors.Keys);
    }

    [Fact]
    public void D2_NullItemId_IsRejected()
    {
        var errors = CustomerSchedulePickupHandler.BuildCartItemErrors(
            [Cart(null, null)],
            new HashSet<Guid>(),
            new HashSet<Guid>());

        Assert.Contains("cartItems[0].itemId", errors.Keys);
    }

    [Fact]
    public void D2_UnknownServiceId_IsRejected()
    {
        var item = Guid.NewGuid();
        var bogusSvc = Guid.NewGuid();

        var errors = CustomerSchedulePickupHandler.BuildCartItemErrors(
            [Cart(item, bogusSvc)],
            new HashSet<Guid> { item },
            new HashSet<Guid>());

        Assert.Contains("cartItems[0].serviceId", errors.Keys);
        Assert.DoesNotContain("cartItems[0].itemId", errors.Keys);
    }

    // ── DEFECT 3: customer pickup-cancel transitions ────────────────────────────

    [Theory]
    [InlineData("pending", CancelPickupOutcome.Cancelled)]
    [InlineData("assigned", CancelPickupOutcome.Cancelled)]
    [InlineData("cancelled", CancelPickupOutcome.AlreadyTerminal)]
    [InlineData("completed", CancelPickupOutcome.AlreadyTerminal)]
    [InlineData("converted", CancelPickupOutcome.AlreadyTerminal)]
    [InlineData("rider_dispatched", CancelPickupOutcome.NotCancellable)]
    [InlineData("arrived", CancelPickupOutcome.NotCancellable)]
    public void D3_CancelOutcome_MatchesStatus(string status, CancelPickupOutcome expected)
        => Assert.Equal(expected, CancelPickupByCustomerHandler.DecideOutcome(status));

    // ── DEFECT 4: rider tasks join mapping (pickup leg via pickup request) ───────

    private static DeliveryAssignment PickupLeg() => new()
    {
        Id = Guid.NewGuid(),
        BrandId = Guid.NewGuid(),
        StoreId = Guid.NewGuid(),
        RiderId = Guid.NewGuid(),
        LegType = "pickup",
        Status = "assigned",
        AddressSnapshot = "{}",
        Metadata = "{}",
        PickupRequestId = Guid.NewGuid(),
        AssignedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void D4_PickupLeg_UsesPickupRequestNotPlaceholders()
    {
        var da = PickupLeg();
        var pr = new PickupRequest
        {
            Id = da.PickupRequestId!.Value,
            RequestNumber = "PKP-2026-XXXX-000004",
            BrandId = da.BrandId,
            CustomerId = Guid.NewGuid(),
            AddressId = Guid.NewGuid(),
            PickupDate = new DateOnly(2026, 6, 13),
            PickupWindowStart = new TimeOnly(9, 30),
            EstimatedItems = 4,
            EstimatedAmount = 250m,
            Status = "assigned",
            Metadata = "{}",
        };

        var dto = RiderTaskMapper.ToDto(da, o: null, c: null, addr: null,
            payout: new RiderPayoutSettings(), pr: pr);

        // Real values instead of the "—"/0 placeholders that the broken order join produced.
        Assert.Equal("PKP-2026-XXXX-000004", dto.OrderNumber);
        Assert.Equal(4, dto.GarmentCount);
        Assert.Equal(250m, dto.AmountDue);
        Assert.Equal("09:30", dto.ScheduledTime);
    }

    [Fact]
    public void D4_PickupLeg_NoPickupRequest_StillFallsBackGracefully()
    {
        var da = PickupLeg();
        var dto = RiderTaskMapper.ToDto(da, o: null, c: null, addr: null,
            payout: new RiderPayoutSettings(), pr: null);

        Assert.Equal("—", dto.OrderNumber);   // no source data at all → placeholder
        Assert.Equal(0, dto.GarmentCount);
    }

    // ── DEFECT 6: pickup completion advances order through legal steps ───────────

    [Fact]
    public void D6_ForwardPath_PickupScheduled_To_PickedUp_WalksLegalHops()
    {
        var hops = OrderStateMachine.ForwardPath(OrderStatus.PickupScheduled, OrderStatus.PickedUp);
        Assert.Equal(
            new[] { OrderStatus.PickupAssigned, OrderStatus.PickupInProgress, OrderStatus.PickedUp },
            hops);
    }

    [Fact]
    public void D6_ForwardPath_PickupScheduled_To_Received()
    {
        var hops = OrderStateMachine.ForwardPath(OrderStatus.PickupScheduled, OrderStatus.Received);
        Assert.Equal(OrderStatus.Received, hops[^1]);
        // Every hop must be a legal single-step transition.
        var from = OrderStatus.PickupScheduled;
        foreach (var to in hops)
        {
            OrderStateMachine.ValidateTransition(from, to); // throws if illegal
            from = to;
        }
    }

    [Fact]
    public void D6_ForwardPath_AlreadyAtOrBeyondTarget_IsNoOp()
    {
        Assert.Empty(OrderStateMachine.ForwardPath(OrderStatus.PickedUp, OrderStatus.PickedUp));
        Assert.Empty(OrderStateMachine.ForwardPath(OrderStatus.Received, OrderStatus.PickedUp));
    }

    // ── DEFECT 7: rider "today" honours IST, not UTC ────────────────────────────

    [Fact]
    public void D7_LocalDay_At_0430Ist_IsTheIstDate_NotTheUtcDate()
    {
        // 2026-06-12 23:00:00Z == 2026-06-13 04:30 IST. The UTC date is the 12th; the
        // local (IST) date is the 13th — the rider's "today".
        var utcInstant = new DateTimeOffset(2026, 6, 12, 23, 0, 0, TimeSpan.Zero);
        var tz = LocalDateRange.Resolve(LocalDateRange.DefaultTimeZoneId);

        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcInstant, tz).DateTime);

        Assert.Equal(new DateOnly(2026, 6, 13), localDate);
        Assert.NotEqual(DateOnly.FromDateTime(utcInstant.UtcDateTime), localDate);
    }
}
