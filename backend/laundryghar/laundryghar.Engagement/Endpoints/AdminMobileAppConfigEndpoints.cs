using laundryghar.Engagement.Application.Cms.Commands;
using laundryghar.Engagement.Application.Cms.Dtos;
using laundryghar.Engagement.Application.Cms.Queries;
using MediatR;

namespace laundryghar.Engagement.Endpoints;

public static class AdminMobileAppConfigEndpoints
{
    public static RouteGroupBuilder MapAdminMobileAppConfigEndpoints(this RouteGroupBuilder group)
    {
        var g = group.MapGroup("/app-config").WithTags("Admin - CMS - Mobile App Config");

        g.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? platform = null) =>
        {
            var r = await sender.Send(new GetMobileAppConfigsQuery(
                page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, platform), ct);
            return Results.Ok(new PaginatedListResponse<MobileAppConfigDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cms.appconfig.manage");

        g.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetMobileAppConfigByIdQuery(id), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<MobileAppConfigDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cms.appconfig.manage");

        g.MapPost("/", async (CreateMobileAppConfigRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateMobileAppConfigCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/app-config/{r.Id}",
                new SingleResponse<MobileAppConfigDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cms.appconfig.manage");

        g.MapPut("/{id:guid}", async (Guid id, UpdateMobileAppConfigRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateMobileAppConfigCommand(id, req, u.UserId), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<MobileAppConfigDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cms.appconfig.manage");

        g.MapDelete("/{id:guid}", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new DeleteMobileAppConfigCommand(id, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:cms.appconfig.manage");

        return group;
    }
}
