using core.Application.Identity.AccessControl.Dtos;
using core.Application.Identity.AccessControl.Queries.GetNavigator;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// Admin — Navigator (/api/v1/admin/navigator). The signed-in user's data-driven sidebar menu,
/// gated by their permissions inside the handler. Authorization only — no specific permission.
/// </summary>
public class Navigator : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/navigator";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Navigator").RequireAuthorization();

        group.MapGet(GetNavigator);
    }

    public static async Task<IResult> GetNavigator(IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetNavigatorQuery(), ct);
        return Results.Ok(new SingleResponse<NavigatorDto> { Status = true, Data = data });
    }
}
