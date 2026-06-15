using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Warehouse.Batches.Commands.AddGarmentToBatch;
using operations.Application.Warehouse.Batches.Commands.CreateWarehouseBatch;
using operations.Application.Warehouse.Batches.Commands.RemoveGarmentFromBatch;
using operations.Application.Warehouse.Batches.Commands.UpdateWarehouseBatch;
using operations.Application.Warehouse.Batches.Dtos;
using operations.Application.Warehouse.Batches.Queries.GetBatchById;
using operations.Application.Warehouse.Batches.Queries.GetBatches;

namespace operations.WebApi.Endpoints.Warehouse;

/// <summary>
/// Admin — Warehouse batches: CRUD plus garment add/remove. Thin dispatch through <see cref="IDispatcher"/>.
/// </summary>
public class WarehouseBatches : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/warehouse-batches";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Warehouse Batches")
             .RequireAuthorization("permission:warehouse.batch.manage");

        group.MapGet(GetAll, "/");
        group.MapGet(GetById, "/{id:guid}");
        group.MapPost(Create, "/").AddEndpointFilter<ValidationFilter<CreateWarehouseBatchRequest>>();
        group.MapPut(Update, "/{id:guid}").AddEndpointFilter<ValidationFilter<UpdateWarehouseBatchRequest>>();
        group.MapPost(AddGarment, "/{id:guid}/garments/{garmentId:guid}");
        group.MapDelete(RemoveGarment, "/{id:guid}/garments/{garmentId:guid}");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, string? status = null, Guid? warehouseId = null)
    {
        var data = await dispatcher.QueryAsync(
            new GetBatchesQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status, warehouseId), ct);
        return Results.Ok(new PaginatedListResponse<WarehouseBatchDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetBatchByIdQuery(id), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<WarehouseBatchDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Create(CreateWarehouseBatchRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CreateWarehouseBatchCommand(req, user.UserId), ct);
        return Results.Created($"/api/v1/admin/warehouse-batches/{data.Id}",
            new SingleResponse<WarehouseBatchDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Update(Guid id, UpdateWarehouseBatchRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new UpdateWarehouseBatchCommand(id, req, user.UserId), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<WarehouseBatchDto> { Status = true, Data = data });
    }

    public static async Task<IResult> AddGarment(Guid id, Guid garmentId, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new AddGarmentToBatchCommand(id, garmentId, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    public static async Task<IResult> RemoveGarment(Guid id, Guid garmentId, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new RemoveGarmentFromBatchCommand(id, garmentId, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
