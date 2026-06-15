using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Warehouse.Processes.Commands.CreateWarehouseProcess;
using operations.Application.Warehouse.Processes.Dtos;
using operations.Application.Warehouse.Processes.Queries.GetWarehouseProcesses;

namespace operations.WebApi.Endpoints.Warehouse;

/// <summary>
/// Admin — Warehouse processes lookup. Thin dispatch through <see cref="IDispatcher"/>.
/// </summary>
public class WarehouseProcesses : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/warehouse-processes";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Warehouse Processes").RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:warehouse.process.scan");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateWarehouseProcessRequest>>()
            .RequireAuthorization("permission:warehouse.process.scan");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 50)
    {
        var data = await dispatcher.QueryAsync(
            new GetWarehouseProcessesQuery(page < 1 ? 1 : page, pageSize < 1 ? 50 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<WarehouseProcessDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Create(CreateWarehouseProcessRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CreateWarehouseProcessCommand(req, user.UserId), ct);
        return Results.Created($"/api/v1/admin/warehouse-processes/{data.Id}",
            new SingleResponse<WarehouseProcessDto> { Status = true, Data = data });
    }
}
