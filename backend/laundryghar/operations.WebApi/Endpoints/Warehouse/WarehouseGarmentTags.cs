using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Warehouse.Garments.Commands.GenerateTags;
using operations.Application.Warehouse.Garments.Dtos;
using operations.Application.Warehouse.Garments.Queries.GetTags;

namespace operations.WebApi.Endpoints.Warehouse;

/// <summary>
/// Admin — Garment tags: paged list + bulk generation. Thin dispatch through <see cref="IDispatcher"/>.
/// </summary>
public class WarehouseGarmentTags : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/garment-tags";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Garment Tags").RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:fulfillment.tag");
        group.MapPost(Generate, "/generate")
            .AddEndpointFilter<ValidationFilter<GenerateTagsRequest>>()
            .RequireAuthorization("permission:fulfillment.tag");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 50, string? status = null)
    {
        var data = await dispatcher.QueryAsync(
            new GetTagsQuery(page < 1 ? 1 : page, pageSize < 1 ? 50 : pageSize, status), ct);
        return Results.Ok(new PaginatedListResponse<GarmentTagDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Generate(GenerateTagsRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new GenerateTagsCommand(req, user.UserId), ct);
        return Results.Ok(new ListResponse<GarmentTagDto> { Status = true, Data = data.ToList() });
    }
}
