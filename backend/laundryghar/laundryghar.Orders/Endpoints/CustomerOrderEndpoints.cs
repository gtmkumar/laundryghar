using laundryghar.Orders.Application.Delivery.Dtos;
using laundryghar.Orders.Application.Delivery.Queries;
using laundryghar.Orders.Application.Orders.Commands;
using laundryghar.Orders.Application.Orders.Dtos;
using laundryghar.Orders.Application.Orders.Queries;
using laundryghar.Orders.Application.Pickup.Commands;
using laundryghar.Orders.Application.Pickup.Dtos;
using laundryghar.Orders.Application.Pickup.Queries;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace laundryghar.Orders.Endpoints;

/// <summary>
/// Customer-facing order endpoints.
/// All require CustomerOnly policy (token_use=customer).
/// Self-filter: customerId is always derived from sub claim (ClaimTypes.NameIdentifier).
/// IDOR protection: every query/command filters by customerId from token, not from URL.
/// </summary>
public static class CustomerOrderEndpoints
{
    public static RouteGroupBuilder MapCustomerOrderEndpoints(this RouteGroupBuilder group)
    {
        // ── Orders ────────────────────────────────────────────────────────────
        var orders = group.MapGroup("/orders").WithTags("Customer - Orders");

        orders.MapGet("/", async (
            HttpContext http, [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20) =>
        {
            var customerId = GetCustomerId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new GetMyOrdersQuery(customerId, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<OrderDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        orders.MapGet("/{id:guid}", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            // IDOR guard: GetMyOrderByIdQuery filters by customerId from token — never from URL
            var r = await sender.Send(new GetMyOrderByIdQuery(id, customerId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<OrderDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        orders.MapGet("/{id:guid}/tracking", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new GetMyOrderTrackingQuery(id, customerId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new ListResponse<OrderStatusHistoryDto> { Status = true, Data = r.ToList() });
        }).RequireAuthorization("CustomerOnly");

        orders.MapPost("/{id:guid}/cancel", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            // Inject customer as user for RequireBrandId() — use BrandId from token
            var brandId = GetBrandId(http);
            // For cancel, we wrap in a custom MediatR command that carries the brand
            // We delegate to CancelOrderCommand with IsCustomer=true (state machine enforces customer rules)
            var r = await sender.Send(
                new CancelOrderByCustomerCommand(id, customerId, brandId, null), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<OrderDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        orders.MapPost("/{id:guid}/rate", async (Guid id, HttpContext http, RateOrderRequest req, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var result = await sender.Send(new RateOrderCommand(id, customerId, req), ct);
            return result.Kind switch
            {
                RateOrderResultKind.NotFound => Results.NotFound(new Response { Status = false }),
                RateOrderResultKind.InvalidStatus => Results.UnprocessableEntity(new Response { Status = false }),
                _ => Results.Ok(new SingleResponse<OrderDto> { Status = true, Data = result.Order })
            };
        }).RequireAuthorization("CustomerOnly");

        // ── Pickup scheduling ─────────────────────────────────────────────────
        var pickups = group.MapGroup("/pickup-requests").WithTags("Customer - Pickups");

        pickups.MapPost("/", async (HttpContext http, CreatePickupRequestRequest req, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            var brandId = GetBrandId(http);
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

            var result = await sender.Send(
                new CustomerSchedulePickupCommand(customerId, brandId, req, customerId,
                    ResolvedIdempotencyKey: idempotencyKey,
                    ResolvedSource: source), ct);

            // Idempotent hit → 200 OK (request already existed, not creating a new resource).
            // New creation → 201 Created.
            return result.AlreadyExisted
                ? Results.Ok(new SingleResponse<PickupRequestDto> { Status = true, Data = result.Dto })
                : Results.Created($"/api/v1/customer/pickup-requests/{result.Dto.Id}",
                    new SingleResponse<PickupRequestDto> { Status = true, Data = result.Dto });
        }).RequireAuthorization("CustomerOnly");

        /// <summary>GET customer's own pickup requests — self-filtered by JWT sub.</summary>
        pickups.MapGet("/", async (
            HttpContext http, [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? status = null) =>
        {
            var customerId = GetCustomerId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(
                new GetMyPickupRequestsQuery(customerId, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status), ct);
            return Results.Ok(new PaginatedListResponse<PickupRequestDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        /// <summary>GET a single pickup request by id — IDOR-guarded: customer_id must match JWT sub.</summary>
        pickups.MapGet("/{id:guid}", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new GetMyPickupRequestByIdQuery(id, customerId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PickupRequestDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        /// <summary>
        /// POST /pickup-requests/{id}/reschedule — move a pending/no_response/rescheduled
        /// pickup request to a new date (+ optional new slot).
        /// IDOR-guarded: customerId from JWT must match the pickup request's customer_id.
        /// </summary>
        pickups.MapPost("/{id:guid}/reschedule", async (
            Guid id, HttpContext http, ReschedulePickupRequest req, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            var brandId    = GetBrandId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();

            var r = await sender.Send(
                new ReschedulePickupCommand(id, customerId, brandId, req, customerId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PickupRequestDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        // ── Coupon validation (preview only — does not redeem) ────────────────
        var coupons = group.MapGroup("/coupons").WithTags("Customer - Coupons");

        /// <summary>
        /// POST /coupons/validate — validates a coupon code against the customer's
        /// brand and estimated cart total, returning a discount preview.
        /// Does NOT create a redemption record; use this for the checkout UI preview.
        /// </summary>
        coupons.MapPost("/validate", async (
            HttpContext http, ValidateCouponRequest req, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            var brandId    = GetBrandId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();

            var r = await sender.Send(
                new ValidateCouponForPickupQuery(customerId, brandId, req.CouponCode, req.EstimatedSubtotal ?? 0m), ct);
            return Results.Ok(new SingleResponse<CouponPreviewResult> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        // ── Delivery slots ────────────────────────────────────────────────────
        var slots = group.MapGroup("/delivery-slots").WithTags("Customer - Delivery Slots");

        slots.MapGet("/", async (Guid? storeId, string? date, ISender sender, CancellationToken ct) =>
        {
            DateOnly? slotDate = date is not null && DateOnly.TryParse(date, out var d) ? d : null;
            var r = await sender.Send(new GetAvailableSlotsQuery(storeId, slotDate), ct);
            return Results.Ok(new ListResponse<DeliverySlotDto> { Status = true, Data = r.ToList() });
        }).RequireAuthorization("CustomerOnly");

        return group;
    }

    private static Guid GetCustomerId(HttpContext http)
    {
        var sub = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    private static Guid GetBrandId(HttpContext http)
    {
        var raw = http.User.FindFirstValue("brand_id");
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}
