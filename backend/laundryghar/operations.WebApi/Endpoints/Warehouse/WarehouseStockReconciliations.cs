using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Warehouse.StockReconciliation.Commands.AddReconItem;
using operations.Application.Warehouse.StockReconciliation.Commands.CloseStockRecon;
using operations.Application.Warehouse.StockReconciliation.Commands.CreateStockRecon;
using operations.Application.Warehouse.StockReconciliation.Dtos;
using operations.Application.Warehouse.StockReconciliation.Queries.GetStockReconById;
using operations.Application.Warehouse.StockReconciliation.Queries.GetStockRecons;

namespace operations.WebApi.Endpoints.Warehouse;

/// <summary>
/// Admin — Stock reconciliation sessions: create, list, by-id, add scanned item, close (which
/// triggers the lost-garment flow). Thin dispatch through <see cref="IDispatcher"/>.
/// </summary>
public class WarehouseStockReconciliations : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/stock-reconciliations";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Stock Reconciliation")
             .RequireAuthorization("permission:stockrecon.manage");

        group.MapGet(GetAll, "/");
        group.MapGet(GetById, "/{id:guid}");
        group.MapPost(Create, "/").AddEndpointFilter<ValidationFilter<CreateStockReconciliationRequest>>();
        group.MapPost(AddItem, "/{id:guid}/items").AddEndpointFilter<ValidationFilter<AddReconItemRequest>>();
        group.MapPost(Close, "/{id:guid}/close");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, string? status = null)
    {
        var data = await dispatcher.QueryAsync(
            new GetStockReconsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status), ct);
        return Results.Ok(new PaginatedListResponse<StockReconciliationDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetStockReconByIdQuery(id), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<StockReconciliationDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Create(CreateStockReconciliationRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CreateStockReconCommand(req, user.UserId), ct);
        return Results.Created($"/api/v1/admin/stock-reconciliations/{data.Id}",
            new SingleResponse<StockReconciliationDto> { Status = true, Data = data });
    }

    public static async Task<IResult> AddItem(Guid id, AddReconItemRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new AddReconItemCommand(id, req, user.UserId), ct);
        return data is null
            ? Results.NotFound()
            : Results.Created($"/api/v1/admin/stock-reconciliations/{id}/items/{data.Id}",
                new SingleResponse<StockReconciliationItemDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Close(Guid id, CloseReconRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CloseStockReconCommand(id, req, user.UserId), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<StockReconciliationDto> { Status = true, Data = data });
    }
}
