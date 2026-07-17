using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Caching;
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

    // Output-cache tag. This config is built from code-registered strategies (static per deploy),
    // so there is no admin write to evict against — the 1h TTL is the sole refresh bound.
    private const string ConfigTag = "config:fulfillment";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Fulfilment - Config");

        // Any authenticated client may read the config it needs to mount its feature pack.
        // Cached identical per deploy; the {mode} route value is part of the request path, so
        // each mode gets its own cache entry under the framework's default (path-based) key —
        // no SetVaryByRouteValue needed. No query params are read by either handler.
        group.MapGet(GetAll, "/").RequireAuthorization()
            .CacheSharedOutput(ConfigTag, TimeSpan.FromHours(1));
        group.MapGet(GetByMode, "/{mode}").RequireAuthorization()
            .CacheSharedOutput(ConfigTag, TimeSpan.FromHours(1));
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
