using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using Microsoft.AspNetCore.Mvc;
using operations.Application.Orders.Ops;
using operations.Application.Orders.Orders.Commands;
using operations.Application.Orders.Orders.Dtos;
using operations.Application.Orders.Orders.Queries;

namespace operations.WebApi.Endpoints.Orders;

/// <summary>Admin — orders board, status/cancel mutations, notes, and ops queues.
/// Per-route permission policies; brand scoping in handlers.</summary>
public class AdminOrderEndpoints : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/orders";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Orders");

        // R3-SEC-2: POS-shared routes accept EITHER the admin Orders family OR the POS family.
        // Pipe-syntax policy ("permission:a|b") uses AnyPermissionRequirement so that store_admin /
        // store_staff holding pos.order.read/create can reach these endpoints without being granted
        // the broader orders.* permissions that unlock the full admin Orders module.
        group.MapGet(GetOrders, "/").RequireAuthorization("permission:orders.list|pos.order.read");
        group.MapGet(GetOrderById, "/{id:guid}").RequireAuthorization("permission:orders.read|pos.order.read");
        group.MapPost(CreateOrder, "/")
            .AddEndpointFilter<ValidationFilter<CreateOrderRequest>>()
            .RequireAuthorization("permission:orders.create|pos.order.create");
        group.MapPatch(UpdateStatus, "/{id:guid}/status").RequireAuthorization("permission:orders.status.update");
        group.MapPost(Cancel, "/{id:guid}/cancel").RequireAuthorization("permission:orders.cancel");

        // Notes
        group.MapGet(GetNotes, "/{id:guid}/notes").RequireAuthorization("permission:orders.read");
        group.MapPost(CreateNote, "/{id:guid}/notes").RequireAuthorization("permission:orders.notes.manage");
        group.MapDelete(DeleteNote, "/{id:guid}/notes/{noteId:guid}").RequireAuthorization("permission:orders.notes.manage");

        // Ops queues (due today / overdue / stuck)
        group.MapGet(OpsQueues, "/ops-queues").RequireAuthorization("permission:orders.read");
    }

    public static async Task<IResult> GetOrders(
        IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, string? status = null, Guid? storeId = null,
        string? dateFrom = null, string? dateTo = null, string? statusGroup = null)
    {
        DateOnly? from = dateFrom is not null && DateOnly.TryParse(dateFrom, out var df) ? df : null;
        DateOnly? to   = dateTo   is not null && DateOnly.TryParse(dateTo, out var dt)   ? dt : null;
        var r = await dispatcher.QueryAsync(new GetOrdersQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status, storeId, from, to, statusGroup), ct);
        return Results.Ok(new PaginatedListResponse<OrderDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetOrderById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetOrderByIdQuery(id), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<OrderDto> { Status = true, Data = r });
    }

    public static async Task<IResult> CreateOrder(HttpContext http, CreateOrderRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        // B2: thread Idempotency-Key header so POS retries are deduplicated.
        var idempotencyKey = http.Request.Headers.TryGetValue("Idempotency-Key", out var hdrKey)
            ? hdrKey.FirstOrDefault()
            : null;

        var r = await dispatcher.SendAsync(new CreateOrderCommand(req, u.UserId, idempotencyKey), ct);
        return Results.Created($"/api/v1/admin/orders/{r.Id}",
            new SingleResponse<OrderDto> { Status = true, Data = r });
    }

    public static async Task<IResult> UpdateStatus(Guid id, UpdateOrderStatusRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdateOrderStatusCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<OrderDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Cancel(Guid id, HttpContext http, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var reason = await http.Request.ReadFromJsonAsync<CancelReasonBody>(ct);
        var r = await dispatcher.SendAsync(new CancelOrderCommand(id, reason?.Reason, false, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<OrderDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetNotes(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetOrderNotesQuery(id), ct);
        return Results.Ok(new ListResponse<OrderNoteDto> { Status = true, Data = r.ToList() });
    }

    public static async Task<IResult> CreateNote(Guid id, CreateOrderNoteRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateOrderNoteCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Created($"/api/v1/admin/orders/{id}/notes/{r.Id}",
            new SingleResponse<OrderNoteDto> { Status = true, Data = r });
    }

    public static async Task<IResult> DeleteNote(Guid id, Guid noteId, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteOrderNoteCommand(id, noteId, u.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    // GET /api/v1/admin/orders/ops-queues?page=1&pageSize=20&storeId=...
    // Returns three independently-paged buckets with badge counts.
    public static async Task<IResult> OpsQueues(
        IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, Guid? storeId = null)
    {
        var r = await dispatcher.QueryAsync(new OpsQueuesQuery(page, pageSize, storeId), ct);
        return Results.Ok(new SingleResponse<OpsQueuesResponse> { Status = true, Data = r });
    }

    private sealed record CancelReasonBody(string? Reason);
}
