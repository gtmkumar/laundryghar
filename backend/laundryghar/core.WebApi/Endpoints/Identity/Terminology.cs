using core.Application.Identity.TenancyOrg.Terminology;
using laundryghar.Utilities.Caching;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// The per-vertical vocabulary every client renders with (PLATFORM_STRATEGY.md §3).
///
/// <para>Anonymous on purpose: the customer app needs the right words on its very first screen,
/// before anyone has signed in. Nothing here is tenant data — terminology is per VERTICAL, shared by
/// every brand in it — so there is nothing to leak, and the response is cacheable across tenants.</para>
/// </summary>
public class Terminology : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/terminology";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Terminology");

        // Changes only when the catalogue is re-seeded, and is identical for every brand in a
        // vertical — so a long shared TTL keyed on the vertical is right.
        group.MapGet(Get, "").AllowAnonymous()
             .CacheSharedOutput("terminology", TimeSpan.FromMinutes(30), "verticalKey");
    }

    public static async Task<IResult> Get(IDispatcher dispatcher, CancellationToken ct, string? verticalKey = null)
    {
        var data = await dispatcher.QueryAsync(new GetTerminologyQuery(verticalKey), ct);
        return Results.Ok(new SingleResponse<TerminologyPackDto> { Status = true, Data = data });
    }
}
