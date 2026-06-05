using laundryghar.Orders.Application.Delivery.Commands;
using laundryghar.Orders.Application.Delivery.Dtos;
using laundryghar.Orders.Application.Delivery.Queries;
using laundryghar.Orders.Application.Pickup.Commands;
using laundryghar.Orders.Application.Pickup.Dtos;
using laundryghar.Orders.Application.Pickup.Queries;
using MediatR;

namespace laundryghar.Orders.Endpoints;

public static class AdminPickupEndpoints
{
    public static RouteGroupBuilder MapAdminPickupEndpoints(this RouteGroupBuilder group)
    {
        // ── Pickup requests ───────────────────────────────────────────────────
        var pickups = group.MapGroup("/pickup-requests").WithTags("Admin - Pickups");

        pickups.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? status = null) =>
        {
            var r = await sender.Send(new GetPickupRequestsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status), ct);
            return Results.Ok(new PaginatedListResponse<PickupRequestDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:pickup.read");

        pickups.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetPickupRequestByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PickupRequestDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:pickup.read");

        pickups.MapPost("/", async (CreatePickupRequestRequest req, Guid customerId, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreatePickupRequestAdminCommand(req, customerId, u.UserId), ct);
            return Results.Created($"/api/v1/admin/pickup-requests/{r.Id}",
                new SingleResponse<PickupRequestDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:pickup.create");

        pickups.MapPost("/{id:guid}/assign", async (Guid id, AssignPickupRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new AssignPickupCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<DeliveryAssignmentDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:pickup.assign");

        // ── Delivery slots ────────────────────────────────────────────────────
        var slots = group.MapGroup("/delivery-slots").WithTags("Admin - Delivery Slots");

        slots.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            Guid? storeId = null, string? date = null, string? slotType = null, int page = 1, int pageSize = 50) =>
        {
            DateOnly? slotDate = date is not null && DateOnly.TryParse(date, out var d) ? d : null;
            var r = await sender.Send(new GetDeliverySlotsQuery(storeId, slotDate, slotType, page < 1 ? 1 : page, pageSize < 1 ? 50 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<DeliverySlotDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:delivery.slot.read");

        slots.MapPost("/", async (CreateDeliverySlotRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateDeliverySlotCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/delivery-slots/{r.Id}",
                new SingleResponse<DeliverySlotDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:delivery.slot.manage");

        slots.MapPut("/{id:guid}", async (Guid id, UpdateDeliverySlotRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateDeliverySlotCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<DeliverySlotDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:delivery.slot.manage");

        // ── Delivery assignments ──────────────────────────────────────────────
        var assigns = group.MapGroup("/delivery-assignments").WithTags("Admin - Delivery Assignments");

        assigns.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20) =>
        {
            var r = await sender.Send(new GetDeliveryAssignmentsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<DeliveryAssignmentDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:delivery.assign");

        assigns.MapPost("/", async (CreateDeliveryAssignmentRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateDeliveryAssignmentCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/delivery-assignments/{r.Id}",
                new SingleResponse<DeliveryAssignmentDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:delivery.assign");

        assigns.MapPut("/{id:guid}", async (Guid id, UpdateDeliveryAssignmentRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateDeliveryAssignmentCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<DeliveryAssignmentDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:delivery.assign");

        return group;
    }
}
