using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using operations.Application.Orders.Pickup.Commands;
using operations.Application.Orders.Pickup.Dtos;
using operations.Application.Orders.Pickup.Queries;

namespace operations.WebApi.Endpoints.Orders;

/// <summary>Admin — pickup requests (list/detail/create/assign/reject). Per-route permission policies.</summary>
public class AdminPickupRequestEndpoints : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/pickup-requests";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Pickups");

        group.MapGet(GetAll, "/").RequireAuthorization("permission:pickup.read");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:pickup.read");
        group.MapPost(Create, "/").RequireAuthorization("permission:pickup.create");
        group.MapPost(Assign, "/{id:guid}/assign").RequireAuthorization("permission:pickup.assign");
        // Permission: pickup.assign — no dedicated pickup.reject code exists; reusing pickup.assign
        // is the correct choice because rejection and assignment are the two administrative
        // disposition actions on a pending request and are always held by the same role.
        group.MapPost(Reject, "/{id:guid}/reject").RequireAuthorization("permission:pickup.assign");
    }

    public static async Task<IResult> GetAll(
        IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, string? status = null)
    {
        var r = await dispatcher.QueryAsync(new GetPickupRequestsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status), ct);
        return Results.Ok(new PaginatedListResponse<PickupRequestDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetPickupRequestByIdQuery(id), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PickupRequestDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreatePickupRequestRequest req, Guid customerId, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreatePickupRequestAdminCommand(req, customerId, u.UserId), ct);
        return Results.Created($"/api/v1/admin/pickup-requests/{r.Id}",
            new SingleResponse<PickupRequestDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Assign(Guid id, AssignPickupRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new AssignPickupCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<DeliveryAssignmentDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Reject(Guid id, RejectPickupRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new RejectPickupCommand(id, req.Reason, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PickupRequestDto> { Status = true, Data = r });
    }
}
