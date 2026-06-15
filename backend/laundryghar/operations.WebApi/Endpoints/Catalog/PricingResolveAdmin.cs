using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using operations.Application.Catalog.Pricing.Dtos;
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
