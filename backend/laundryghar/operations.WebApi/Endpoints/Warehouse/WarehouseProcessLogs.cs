using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Warehouse.Processes.Commands.CreateProcessLog;
using operations.Application.Warehouse.Processes.Dtos;
using operations.Application.Warehouse.Processes.Queries.GetProcessLogs;

namespace operations.WebApi.Endpoints.Warehouse;

/// <summary>
/// Admin — Process logs (scan events). Thin dispatch through <see cref="IDispatcher"/>.
/// </summary>
public class WarehouseProcessLogs : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/process-logs";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Process Logs").RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:warehouse.process.scan");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateProcessLogRequest>>()
            .RequireAuthorization("permission:warehouse.process.scan");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        Guid? garmentId = null, Guid? batchId = null, int page = 1, int pageSize = 50)
    {
        var data = await dispatcher.QueryAsync(
            new GetProcessLogsQuery(garmentId, batchId, page < 1 ? 1 : page, pageSize < 1 ? 50 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<ProcessLogEntryDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Create(CreateProcessLogRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CreateProcessLogCommand(req, user.UserId), ct);
        return Results.Created($"/api/v1/admin/process-logs/{data.Id}",
            new SingleResponse<ProcessLogEntryDto> { Status = true, Data = data });
    }
}
