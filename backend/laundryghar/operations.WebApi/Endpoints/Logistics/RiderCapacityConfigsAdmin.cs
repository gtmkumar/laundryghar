using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Logistics.CapacityConfigs.Commands.CreateCapacityConfig;
using operations.Application.Logistics.CapacityConfigs.Commands.UpdateCapacityConfig;
using operations.Application.Logistics.CapacityConfigs.Dtos;
using operations.Application.Logistics.CapacityConfigs.Queries.GetCapacityConfigById;
using operations.Application.Logistics.CapacityConfigs.Queries.GetCapacityConfigs;

namespace operations.WebApi.Endpoints.Logistics;

/// <summary>
/// Admin — Rider capacity configs: per-rider slot capacity (pickups/deliveries/concurrent).
/// Paged list, by-id, create, update. Thin dispatch through <see cref="IDispatcher"/>.
/// </summary>
public class RiderCapacityConfigsAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/rider-capacity-configs";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Rider Capacity Configs").RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:rider.capacity.manage");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:rider.capacity.manage");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateCapacityConfigRequest>>()
            .RequireAuthorization("permission:rider.capacity.manage");
        group.MapPut(Update, "/{id:guid}").RequireAuthorization("permission:rider.capacity.manage");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, Guid? riderId = null, string? status = null)
    {
        var r = await dispatcher.QueryAsync(
            new GetCapacityConfigsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, riderId, status), ct);
        return Results.Ok(new PaginatedListResponse<RiderCapacityConfigDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetCapacityConfigByIdQuery(id), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderCapacityConfigDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreateCapacityConfigRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateCapacityConfigCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/rider-capacity-configs/{r.Id}",
            new SingleResponse<RiderCapacityConfigDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdateCapacityConfigRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdateCapacityConfigCommand(id, req, u.UserId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderCapacityConfigDto> { Status = true, Data = r });
    }
}
