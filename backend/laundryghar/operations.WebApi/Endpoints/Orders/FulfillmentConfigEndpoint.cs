using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using operations.Application.Fulfillment.Queries;

namespace operations.WebApi.Endpoints.Orders;

/// <summary>
/// Client-driven fulfilment configuration (Phase 3): exposes each fulfilment mode's stage
/// descriptors / leg topology so mobile/POS/admin render the right tracking ladder + pack at
/// runtime without hardcoding a laundry status enum. Built live from the registered strategies,
/// so a new vertical (salon) appears here automatically.
/// </summary>
public class FulfillmentConfigEndpoint : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/fulfillment-config";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Fulfilment - Config");

        // Any authenticated client may read the config it needs to mount its feature pack.
        group.MapGet(GetAll, "/").RequireAuthorization();
        group.MapGet(GetByMode, "/{mode}").RequireAuthorization();
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetFulfillmentConfigQuery(), ct);
        return Results.Ok(new ListResponse<FulfillmentConfigDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetByMode(string mode, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetFulfillmentConfigQuery(mode), ct);
        var one = r.FirstOrDefault();
        return one is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<FulfillmentConfigDto> { Status = true, Data = one });
    }
}
