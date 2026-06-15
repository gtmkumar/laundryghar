using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Warehouse.Board.Dtos;
using operations.Application.Warehouse.Board.Queries.GetWarehouseBoard;
using operations.Application.Warehouse.Garments.Commands.CreateGarment;
using operations.Application.Warehouse.Garments.Dtos;
using operations.Application.Warehouse.Garments.Queries.GetGarmentById;
using operations.Application.Warehouse.Garments.Queries.GetGarments;
using operations.Application.Warehouse.Garments.Queries.GetGarmentByTag;

namespace operations.WebApi.Endpoints.Warehouse;

/// <summary>
/// Admin — Warehouse garments: kanban board read model, paged list, by-id, by-tag journey, and
/// create-from-order-item. Thin: each method dispatches a command/query through <see cref="IDispatcher"/>.
/// </summary>
public class WarehouseGarments : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/garments";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Garments").RequireAuthorization();

        group.MapGet(GetBoard, "/board").RequireAuthorization("permission:garment.read");
        group.MapGet(GetAll, "/").RequireAuthorization("permission:garment.read");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:garment.read");
        group.MapGet(GetByTag, "/by-tag/{tagCode}").RequireAuthorization("permission:garment.read");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateGarmentRequest>>()
            .RequireAuthorization("permission:garment.tag");
    }

    public static async Task<IResult> GetBoard(IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetWarehouseBoardQuery(), ct);
        return Results.Ok(new SingleResponse<WarehouseBoardDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, string? stage = null, Guid? storeId = null, Guid? batchId = null)
    {
        var data = await dispatcher.QueryAsync(
            new GetGarmentsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, stage, storeId, batchId), ct);
        return Results.Ok(new PaginatedListResponse<GarmentDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetGarmentByIdQuery(id), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<GarmentDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetByTag(string tagCode, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetGarmentByTagQuery(tagCode), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<GarmentJourneyDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Create(CreateGarmentRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CreateGarmentCommand(req, user.UserId), ct);
        return Results.Created($"/api/v1/admin/garments/{data.Id}",
            new SingleResponse<GarmentDto> { Status = true, Data = data });
    }
}
