using laundryghar.Orders.Application.Orders.Commands;
using laundryghar.Orders.Application.Orders.Dtos;
using laundryghar.Orders.Application.Orders.Queries;
using MediatR;

namespace laundryghar.Orders.Endpoints;

public static class AdminOrderEndpoints
{
    public static RouteGroupBuilder MapAdminOrderEndpoints(this RouteGroupBuilder group)
    {
        var orders = group.MapGroup("/orders").WithTags("Admin - Orders");

        orders.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? status = null, Guid? storeId = null,
            string? dateFrom = null, string? dateTo = null) =>
        {
            DateOnly? from = dateFrom is not null && DateOnly.TryParse(dateFrom, out var df) ? df : null;
            DateOnly? to   = dateTo   is not null && DateOnly.TryParse(dateTo, out var dt)   ? dt : null;
            var r = await sender.Send(new GetOrdersQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status, storeId, from, to), ct);
            return Results.Ok(new PaginatedListResponse<OrderDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:orders.list");

        orders.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetOrderByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<OrderDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:orders.read");

        orders.MapPost("/", async (CreateOrderRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateOrderCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/orders/{r.Id}",
                new SingleResponse<OrderDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:orders.create");

        orders.MapPatch("/{id:guid}/status", async (Guid id, UpdateOrderStatusRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateOrderStatusCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<OrderDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:orders.status.update");

        orders.MapPost("/{id:guid}/cancel", async (Guid id, HttpContext http, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var reason = await http.Request.ReadFromJsonAsync<CancelReasonBody>(ct);
            var r = await sender.Send(new CancelOrderCommand(id, reason?.Reason, false, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<OrderDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:orders.cancel");

        // Notes
        orders.MapGet("/{id:guid}/notes", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetOrderNotesQuery(id), ct);
            return Results.Ok(new ListResponse<OrderNoteDto> { Status = true, Data = r.ToList() });
        }).RequireAuthorization("permission:orders.read");

        orders.MapPost("/{id:guid}/notes", async (Guid id, CreateOrderNoteRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateOrderNoteCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Created($"/api/v1/admin/orders/{id}/notes/{r.Id}",
                new SingleResponse<OrderNoteDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:orders.notes.manage");

        orders.MapDelete("/{id:guid}/notes/{noteId:guid}", async (Guid id, Guid noteId, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new DeleteOrderNoteCommand(id, noteId, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:orders.notes.manage");

        return group;
    }

    private sealed record CancelReasonBody(string? Reason);
}
