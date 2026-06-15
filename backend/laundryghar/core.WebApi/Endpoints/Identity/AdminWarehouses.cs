using core.Application.Identity.TenancyOrg.Dtos;
using core.Application.Identity.TenancyOrg.Warehouses.Commands.CreateWarehouse;
using core.Application.Identity.TenancyOrg.Warehouses.Commands.DeleteWarehouse;
using core.Application.Identity.TenancyOrg.Warehouses.Commands.UpdateWarehouse;
using core.Application.Identity.TenancyOrg.Warehouses.Queries.GetWarehouses;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// Admin — Warehouse list + create/update/delete (no get-by-id). List filters by brandId +
/// franchiseId. Thin: each method dispatches a command/query through <see cref="IDispatcher"/>.
/// </summary>
public class AdminWarehouses : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/warehouses";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Warehouses").RequireAuthorization();

        group.MapGet(GetAll).RequireAuthorization("permission:warehouses.list");
        group.MapPost(Create).RequireAuthorization("permission:warehouses.create");
        group.MapPut(Update, "{id:guid}").RequireAuthorization("permission:warehouses.update");
        group.MapDelete(Delete, "{id:guid}").RequireAuthorization("permission:warehouses.delete");
    }

    public static async Task<IResult> GetAll(Guid? brandId, Guid? franchiseId, IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var data = await dispatcher.QueryAsync(new GetWarehousesQuery(brandId, franchiseId, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<WarehouseDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Create(CreateWarehouseRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CreateWarehouseCommand(req, user.UserId), ct);
        return Results.Created($"/api/v1/admin/warehouses/{data.Id}",
            new SingleResponse<WarehouseDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Update(Guid id, UpdateWarehouseRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new UpdateWarehouseCommand(id, req, user.UserId), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<WarehouseDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteWarehouseCommand(id, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
