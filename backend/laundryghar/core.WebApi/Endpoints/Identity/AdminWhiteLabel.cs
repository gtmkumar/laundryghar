using core.Application.Identity.WhiteLabel.Queries;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// §4 tier T3: the provider's own branded apps.
///
/// <para>Gated on the <c>white_label_app</c> feature, which only Enterprise includes (§5) — so a
/// provider on a lower tier gets 402 <c>feature_not_in_plan</c> naming exactly what to buy, which is
/// the whole point of the entitlement layer being separate from permissions.</para>
///
/// <para>This returns the per-brand values an Expo build needs. It does not build or submit
/// anything: **OQ-7** — who submits to the stores — is undecided, and the two answers need very
/// different machinery (holding a provider's developer-account credentials, versus handing them a
/// config and instructions). Both need these values, so these values exist.</para>
/// </summary>
public class AdminWhiteLabel : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/white-label";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - White label").RequireAuthorization();

        group.MapGet(GetAppConfig, "apps/{app}").RequireAuthorization("permission:white_label.read");
    }

    public static async Task<IResult> GetAppConfig(
        string app, ICurrentTenant tenant, IDispatcher dispatcher, CancellationToken ct)
    {
        if (tenant.BrandId is not { } brandId) return Results.BadRequest();

        var config = await dispatcher.QueryAsync(new GetAppConfigQuery(brandId, app), ct);
        return config is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<AppConfigDto> { Status = true, Data = config });
    }
}
