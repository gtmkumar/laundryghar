using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Warehouse.Inspections.Commands.CreateGarmentCondition;
using operations.Application.Warehouse.Inspections.Commands.UpdateGarmentCondition;
using operations.Application.Warehouse.Inspections.Dtos;
using operations.Application.Warehouse.Inspections.Queries.GetGarmentConditions;

namespace operations.WebApi.Endpoints.Warehouse;

/// <summary>
/// Admin — Garment conditions lookup CRUD. Thin dispatch through <see cref="IDispatcher"/>.
/// </summary>
public class WarehouseGarmentConditions : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/garment-conditions";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Garment Conditions").RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:fulfillment.inspect");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateGarmentConditionRequest>>()
            .RequireAuthorization("permission:fulfillment.inspect");
        group.MapPut(Update, "/{id:guid}").RequireAuthorization("permission:fulfillment.inspect");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 50)
    {
        var data = await dispatcher.QueryAsync(
            new GetGarmentConditionsQuery(page < 1 ? 1 : page, pageSize < 1 ? 50 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<GarmentConditionDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Create(CreateGarmentConditionRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CreateGarmentConditionCommand(req, user.UserId), ct);
        return Results.Created($"/api/v1/admin/garment-conditions/{data.Id}",
            new SingleResponse<GarmentConditionDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Update(Guid id, UpdateGarmentConditionRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new UpdateGarmentConditionCommand(id, req, user.UserId), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<GarmentConditionDto> { Status = true, Data = data });
    }
}
