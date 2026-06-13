using laundryghar.Engagement.Infrastructure.Services;
using ICurrentUser = laundryghar.Engagement.Infrastructure.Services.ICurrentUser;
using laundryghar.Engagement.Application.Cms.Commands;
using laundryghar.Engagement.Application.Cms.Dtos;
using laundryghar.Engagement.Application.Cms.Queries;
using MediatR;

namespace laundryghar.Engagement.Endpoints;

public static class AdminAppBannerEndpoints
{
    public static RouteGroupBuilder MapAdminAppBannerEndpoints(this RouteGroupBuilder group)
    {
        var g = group.MapGroup("/app-banners").WithTags("Admin - CMS - App Banners");

        g.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? placement = null) =>
        {
            var r = await sender.Send(new GetAppBannersQuery(
                page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, placement), ct);
            return Results.Ok(new PaginatedListResponse<AppBannerDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cms.banner.manage");

        g.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetAppBannerByIdQuery(id), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<AppBannerDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cms.banner.manage");

        g.MapPost("/", async (CreateAppBannerRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateAppBannerCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/app-banners/{r.Id}",
                new SingleResponse<AppBannerDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cms.banner.manage");

        g.MapPut("/{id:guid}", async (Guid id, UpdateAppBannerRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateAppBannerCommand(id, req, u.UserId), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<AppBannerDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cms.banner.manage");

        g.MapDelete("/{id:guid}", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new DeleteAppBannerCommand(id, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:cms.banner.manage");

        return group;
    }
}
