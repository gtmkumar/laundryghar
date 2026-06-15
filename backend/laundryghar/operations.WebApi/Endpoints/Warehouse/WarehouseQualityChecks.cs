using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Warehouse.QualityChecks.Commands.CreateQualityCheck;
using operations.Application.Warehouse.QualityChecks.Dtos;
using operations.Application.Warehouse.QualityChecks.Queries.GetQualityChecks;

namespace operations.WebApi.Endpoints.Warehouse;

/// <summary>
/// Admin — Quality checks. Thin dispatch through <see cref="IDispatcher"/>.
/// </summary>
public class WarehouseQualityChecks : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/quality-checks";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Quality Checks").RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:qc.perform");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateQualityCheckRequest>>()
            .RequireAuthorization("permission:qc.perform");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        Guid? garmentId = null, Guid? batchId = null, int page = 1, int pageSize = 20)
    {
        var data = await dispatcher.QueryAsync(
            new GetQualityChecksQuery(garmentId, batchId, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<QualityCheckDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Create(CreateQualityCheckRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CreateQualityCheckCommand(req, user.UserId), ct);
        return Results.Created($"/api/v1/admin/quality-checks/{data.Id}",
            new SingleResponse<QualityCheckDto> { Status = true, Data = data });
    }
}
