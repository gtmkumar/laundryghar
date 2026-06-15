using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using operations.Application.Orders.Delivery.Commands;
using operations.Application.Orders.Delivery.Dtos;
using operations.Application.Orders.Delivery.Queries;
using operations.Application.Orders.Pickup.Dtos;
using operations.Application.Orders.Pickup.Queries;

namespace operations.WebApi.Endpoints.Orders;

/// <summary>Admin — delivery slots (list/create/update). Per-route permission policies.</summary>
public class AdminDeliverySlotEndpoints : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/delivery-slots";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Delivery Slots");

        group.MapGet(GetAll, "/").RequireAuthorization("permission:delivery.slot.read");
        group.MapPost(Create, "/").RequireAuthorization("permission:delivery.slot.manage");
        group.MapPut(Update, "/{id:guid}").RequireAuthorization("permission:delivery.slot.manage");
    }

    public static async Task<IResult> GetAll(
        IDispatcher dispatcher, CancellationToken ct,
        Guid? storeId = null, string? date = null, string? slotType = null, int page = 1, int pageSize = 50)
    {
        DateOnly? slotDate = date is not null && DateOnly.TryParse(date, out var d) ? d : null;
        var r = await dispatcher.QueryAsync(new GetDeliverySlotsQuery(storeId, slotDate, slotType, page < 1 ? 1 : page, pageSize < 1 ? 50 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<DeliverySlotDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreateDeliverySlotRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateDeliverySlotCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/delivery-slots/{r.Id}",
            new SingleResponse<DeliverySlotDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdateDeliverySlotRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdateDeliverySlotCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<DeliverySlotDto> { Status = true, Data = r });
    }
}

/// <summary>Admin — delivery assignments (list/create/update). Per-route permission policies.</summary>
public class AdminDeliveryAssignmentEndpoints : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/delivery-assignments";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Delivery Assignments");

        group.MapGet(GetAll, "/").RequireAuthorization("permission:delivery.assign");
        group.MapPost(Create, "/").RequireAuthorization("permission:delivery.assign");
        group.MapPut(Update, "/{id:guid}").RequireAuthorization("permission:delivery.assign");
    }

    public static async Task<IResult> GetAll(
        IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20)
    {
        var r = await dispatcher.QueryAsync(new GetDeliveryAssignmentsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<DeliveryAssignmentDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreateDeliveryAssignmentRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateDeliveryAssignmentCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/delivery-assignments/{r.Id}",
            new SingleResponse<DeliveryAssignmentDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdateDeliveryAssignmentRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdateDeliveryAssignmentCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<DeliveryAssignmentDto> { Status = true, Data = r });
    }
}
