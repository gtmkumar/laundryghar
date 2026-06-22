using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using operations.Application.Catalog.Pricing.Commands.Revert;
using operations.Application.Catalog.Pricing.Dtos;
using operations.Application.Catalog.Pricing.Queries.History;
using operations.Application.Catalog.Pricing.Queries.Matrix;
using operations.Application.Catalog.Pricing.Queries.PriceResolution;

namespace operations.WebApi.Endpoints.Catalog;

/// <summary>
/// Admin — price resolution. Returns the effective price via store → franchise → brand fallback.
/// </summary>
public class PricingResolveAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/pricing";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Pricing - Resolve");

        group.MapGet(Resolve, "/resolve").RequireAuthorization("permission:pricing.read");
        group.MapGet(Matrix, "/matrix").RequireAuthorization("permission:pricing.read");
        group.MapGet(History, "/history").RequireAuthorization("permission:pricing.read");
        group.MapPost(Revert, "/history/{id:guid}/revert").RequireAuthorization("permission:pricing.item.manage");
    }

    public static async Task<IResult> Matrix(Guid? storeId, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetPricingMatrixQuery(storeId), ct);
        return Results.Ok(new SingleResponse<PricingMatrixDto> { Status = true, Data = r });
    }

    public static async Task<IResult> History(IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 30)
    {
        var r = await dispatcher.QueryAsync(new GetPricingHistoryQuery(page < 1 ? 1 : page, pageSize < 1 ? 30 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<PricingHistoryEntryDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Revert(Guid id, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new RevertPricingChangeCommand(id, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound(new Response { Status = false });
    }

    public static async Task<IResult> Resolve(Guid itemId, Guid serviceId, Guid? variantId, Guid? storeId,
        IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new ResolvePriceQuery(itemId, serviceId, variantId, storeId), ct);
        return r is null
            ? Results.NotFound(new Response { Status = false })
            : Results.Ok(new SingleResponse<PriceResolutionDto> { Status = true, Data = r });
    }
}
