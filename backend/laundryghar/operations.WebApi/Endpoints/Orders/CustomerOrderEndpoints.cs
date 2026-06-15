using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using operations.Application.Orders.Delivery.Dtos;
using operations.Application.Orders.Delivery.Queries;
using operations.Application.Orders.Fare.Dtos;
using operations.Application.Orders.Fare.Queries;
using operations.Application.Orders.Orders.Commands;
using operations.Application.Orders.Orders.Dtos;
using operations.Application.Orders.Orders.Queries;
using operations.Application.Orders.Pickup.Commands;
using operations.Application.Orders.Pickup.Dtos;
using operations.Application.Orders.Pickup.Queries;
using operations.Application.Orders.Support;

namespace operations.WebApi.Endpoints.Orders;

/// <summary>
/// Customer-facing order endpoints (orders, support, pickups, coupons, parcel/fare, slots).
/// All require the CustomerOnly policy (token_use=customer).
/// Self-filter: customerId is always derived from the JWT sub (ICurrentUser.UserId), brandId
/// from the brand_id claim (ICurrentUser.BrandId) — never from the URL/body.
/// IDOR protection: every query/command filters by customerId from token, not from URL.
/// </summary>
public class CustomerOrderEndpoints : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/customer";

    public static void Map(RouteGroupBuilder group)
    {
        group.RequireAuthorization("CustomerOnly");

        // ── Orders ────────────────────────────────────────────────────────────
        group.MapGet(GetMyOrders, "/orders").WithTags("Customer - Orders");
        group.MapGet(GetMyOrderById, "/orders/{id:guid}").WithTags("Customer - Orders");
        group.MapGet(GetMyOrderTracking, "/orders/{id:guid}/tracking").WithTags("Customer - Orders");
        group.MapPost(CancelMyOrder, "/orders/{id:guid}/cancel").WithTags("Customer - Orders");
        group.MapPost(RateOrder, "/orders/{id:guid}/rate").WithTags("Customer - Orders");
        group.MapPost(RateRider, "/orders/{id:guid}/rate-rider").WithTags("Customer - Orders");
        group.MapPost(CreateParcelOrder, "/orders/parcel").WithTags("Customer - Orders");

        // ── Customer support tickets ──────────────────────────────────────────
        group.MapPost(CreateTicket, "/support/tickets").WithTags("Customer - Support");
        group.MapGet(GetMyTickets, "/support/tickets").WithTags("Customer - Support");
        group.MapGet(GetTicketDetail, "/support/tickets/{id:guid}").WithTags("Customer - Support");
        group.MapPost(PostTicketMessage, "/support/tickets/{id:guid}/messages").WithTags("Customer - Support");

        // ── Pickup scheduling ─────────────────────────────────────────────────
        group.MapPost(SchedulePickup, "/pickup-requests").WithTags("Customer - Pickups");
        group.MapGet(GetMyPickups, "/pickup-requests").WithTags("Customer - Pickups");
        group.MapGet(GetMyPickupById, "/pickup-requests/{id:guid}").WithTags("Customer - Pickups");
        group.MapPost(ReschedulePickup, "/pickup-requests/{id:guid}/reschedule").WithTags("Customer - Pickups");
        group.MapPost(CancelPickup, "/pickup-requests/{id:guid}/cancel").WithTags("Customer - Pickups");

        // ── Coupon validation (preview only — does not redeem) ────────────────
        group.MapPost(ValidateCoupon, "/coupons/validate").WithTags("Customer - Coupons");

        // ── Fare quote (point-to-point parcel pricing) ────────────────────────
        group.MapPost(FareQuote, "/fare/quote").WithTags("Customer - Fare");

        // ── Delivery slots ────────────────────────────────────────────────────
        group.MapGet(GetAvailableSlots, "/delivery-slots").WithTags("Customer - Delivery Slots");
    }

    // ── Orders ────────────────────────────────────────────────────────────────

    public static async Task<IResult> GetMyOrders(
        ICurrentUser u, IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetMyOrdersQuery(customerId, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<OrderDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetMyOrderById(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        // IDOR guard: GetMyOrderByIdQuery filters by customerId from token — never from URL
        var r = await dispatcher.QueryAsync(new GetMyOrderByIdQuery(id, customerId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<OrderDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetMyOrderTracking(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetMyOrderTrackingQuery(id, customerId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new ListResponse<OrderStatusHistoryDto> { Status = true, Data = r.ToList() });
    }

    public static async Task<IResult> CancelMyOrder(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var brandId = u.BrandId ?? Guid.Empty;
        var r = await dispatcher.SendAsync(new CancelOrderByCustomerCommand(id, customerId, brandId, null), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<OrderDto> { Status = true, Data = r });
    }

    public static async Task<IResult> RateOrder(Guid id, RateOrderRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var result = await dispatcher.SendAsync(new RateOrderCommand(id, customerId, req), ct);
        return result.Kind switch
        {
            RateOrderResultKind.NotFound => Results.NotFound(new Response { Status = false }),
            RateOrderResultKind.InvalidStatus => Results.UnprocessableEntity(new Response { Status = false }),
            _ => Results.Ok(new SingleResponse<OrderDto> { Status = true, Data = result.Order })
        };
    }

    public static async Task<IResult> RateRider(Guid id, RateOrderRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.SendAsync(new RateRiderCommand(id, customerId, req.Score, req.Comment), ct);
        return r.Kind switch
        {
            RateRiderResultKind.NotFound => Results.NotFound(),
            RateRiderResultKind.InvalidStatus => Results.UnprocessableEntity(new Response { Status = false }),
            RateRiderResultKind.NoRider => Results.UnprocessableEntity(new SingleResponse<string> { Status = false, Data = "No rider to rate on this order." }),
            _ => Results.Ok(new SingleResponse<object> { Status = true, Data = new { riderAverage = r.RiderAverage, riderCount = r.RiderCount } })
        };
    }

    // ── Parcel orders (point-to-point) ────────────────────────────────────────
    // A parcel is fare-quoted, so booking creates the order directly (unlike laundry,
    // which schedules a pickup and is converted to an order after weighing).
    public static async Task<IResult> CreateParcelOrder(CreateParcelOrderRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        var brandId    = u.BrandId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();

        var r = await dispatcher.SendAsync(new CreateParcelOrderCommand(customerId, brandId, req), ct);
        return Results.Created($"/api/v1/customer/orders/{r.Id}",
            new SingleResponse<OrderDto> { Status = true, Data = r });
    }

    // ── Support ─────────────────────────────────────────────────────────────

    public static async Task<IResult> CreateTicket(CreateTicketRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        var brandId    = u.BrandId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.SendAsync(new CreateTicketCommand(brandId, "customer", customerId, customerId, null, req), ct);
        return Results.Created($"/api/v1/customer/support/tickets/{r.Ticket.Id}", new SingleResponse<SupportTicketDetailDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetMyTickets(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        var brandId    = u.BrandId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetMyTicketsQuery(brandId, customerId), ct);
        return Results.Ok(new ListResponse<SupportTicketDto> { Status = true, Data = r.ToList() });
    }

    public static async Task<IResult> GetTicketDetail(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetTicketDetailQuery(id, customerId, false), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<SupportTicketDetailDto> { Status = true, Data = r });
    }

    public static async Task<IResult> PostTicketMessage(Guid id, PostMessageRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.SendAsync(new PostTicketMessageCommand(id, "customer", customerId, req.Body, false, customerId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<TicketMessageDto> { Status = true, Data = r });
    }

    // ── Pickup scheduling ─────────────────────────────────────────────────────

    public static async Task<IResult> SchedulePickup(HttpContext http, CreatePickupRequestRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        var brandId    = u.BrandId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();

        // ── Idempotency-Key header ─────────────────────────────────────────
        // Industry-standard header (RFC draft). Prefer header over body field;
        // fall back to req.IdempotencyKey when header is absent.
        var idempotencyKey = http.Request.Headers.TryGetValue("Idempotency-Key", out var hdrKey)
            ? hdrKey.FirstOrDefault()
            : req.IdempotencyKey;

        // ── X-Channel header ──────────────────────────────────────────────
        // Source channel. Header takes precedence over body field.
        var source = http.Request.Headers.TryGetValue("X-Channel", out var hdrChan)
            ? (hdrChan.FirstOrDefault() ?? req.Channel ?? "app")
            : (req.Channel ?? "app");

        var result = await dispatcher.SendAsync(
            new CustomerSchedulePickupCommand(customerId, brandId, req, customerId,
                ResolvedIdempotencyKey: idempotencyKey,
                ResolvedSource: source), ct);

        // Idempotent hit → 200 OK; new creation → 201 Created.
        return result.AlreadyExisted
            ? Results.Ok(new SingleResponse<PickupRequestDto> { Status = true, Data = result.Dto })
            : Results.Created($"/api/v1/customer/pickup-requests/{result.Dto.Id}",
                new SingleResponse<PickupRequestDto> { Status = true, Data = result.Dto });
    }

    public static async Task<IResult> GetMyPickups(
        ICurrentUser u, IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, string? status = null)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(
            new GetMyPickupRequestsQuery(customerId, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status), ct);
        return Results.Ok(new PaginatedListResponse<PickupRequestDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetMyPickupById(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetMyPickupRequestByIdQuery(id, customerId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PickupRequestDto> { Status = true, Data = r });
    }

    public static async Task<IResult> ReschedulePickup(Guid id, ReschedulePickupRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        var brandId    = u.BrandId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();

        var r = await dispatcher.SendAsync(new ReschedulePickupCommand(id, customerId, brandId, req, customerId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PickupRequestDto> { Status = true, Data = r });
    }

    public static async Task<IResult> CancelPickup(Guid id, CancelPickupRequest? req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        var brandId    = u.BrandId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();

        var result = await dispatcher.SendAsync(
            new CancelPickupByCustomerCommand(id, customerId, brandId, req?.Reason, customerId), ct);

        return result.Outcome switch
        {
            CancelPickupOutcome.Cancelled => Results.Ok(
                new SingleResponse<PickupRequestDto> { Status = true, Data = result.Dto }),
            CancelPickupOutcome.NotFound => Results.NotFound(),
            CancelPickupOutcome.AlreadyTerminal => Results.Conflict(
                new SingleResponse<string> { Status = false, Data = result.Reason }),
            CancelPickupOutcome.NotCancellable => Results.UnprocessableEntity(
                new SingleResponse<string> { Status = false, Data = result.Reason }),
            _ => Results.StatusCode(500),
        };
    }

    // ── Coupon validation ─────────────────────────────────────────────────────

    public static async Task<IResult> ValidateCoupon(ValidateCouponRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        var brandId    = u.BrandId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();

        var r = await dispatcher.QueryAsync(
            new ValidateCouponForPickupQuery(customerId, brandId, req.CouponCode, req.EstimatedSubtotal ?? 0m), ct);
        return Results.Ok(new SingleResponse<CouponPreviewResult> { Status = true, Data = r });
    }

    // ── Fare quote ────────────────────────────────────────────────────────────

    public static async Task<IResult> FareQuote(FareQuoteRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        var brandId    = u.BrandId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();

        var r = await dispatcher.QueryAsync(new GetFareQuoteQuery(customerId, brandId, req), ct);
        return Results.Ok(new SingleResponse<FareQuoteDto> { Status = true, Data = r });
    }

    // ── Delivery slots ────────────────────────────────────────────────────────

    public static async Task<IResult> GetAvailableSlots(Guid? storeId, string? date, IDispatcher dispatcher, CancellationToken ct)
    {
        DateOnly? slotDate = date is not null && DateOnly.TryParse(date, out var d) ? d : null;
        var r = await dispatcher.QueryAsync(new GetAvailableSlotsQuery(storeId, slotDate), ct);
        return Results.Ok(new ListResponse<DeliverySlotDto> { Status = true, Data = r.ToList() });
    }
}
