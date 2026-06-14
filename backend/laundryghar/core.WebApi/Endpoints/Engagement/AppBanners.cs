using core.Application.Engagement.Cms.AppBanners.Commands.CreateAppBanner;
using core.Application.Engagement.Cms.AppBanners.Commands.DeleteAppBanner;
using core.Application.Engagement.Cms.AppBanners.Commands.UpdateAppBanner;
using core.Application.Engagement.Cms.AppBanners.Queries.GetAppBannerById;
using core.Application.Engagement.Cms.AppBanners.Queries.GetAppBanners;
using core.Application.Engagement.Cms.Dtos;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace core.WebApi.Endpoints.Engagement;

/// <summary>
/// Admin CMS — App Banner endpoints. Thin: each method dispatches a command/query through
/// <see cref="IDispatcher"/>. No business logic here.
/// </summary>
public class AppBanners : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/app-banners";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - CMS - App Banners")
             .RequireAuthorization("permission:cms.banner.manage");

        group.MapGet(GetAll);
        group.MapGet(GetById, "{id:guid}");
        group.MapPost(Create);
        group.MapPut(Update, "{id:guid}");
        group.MapDelete(Delete, "{id:guid}");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, string? placement = null)
    {
        var data = await dispatcher.QueryAsync(
            new GetAppBannersQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, placement), ct);
        return Results.Ok(new PaginatedListResponse<AppBannerDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetAppBannerByIdQuery(id), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<AppBannerDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Create(CreateAppBannerRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CreateAppBannerCommand(req, user.UserId), ct);
        return Results.Created($"/api/v1/admin/app-banners/{data.Id}",
            new SingleResponse<AppBannerDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Update(Guid id, UpdateAppBannerRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new UpdateAppBannerCommand(id, req, user.UserId), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<AppBannerDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteAppBannerCommand(id, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
