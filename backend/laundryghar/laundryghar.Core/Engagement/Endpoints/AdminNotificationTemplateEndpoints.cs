using laundryghar.Engagement.Infrastructure.Services;
using ICurrentUser = laundryghar.Engagement.Infrastructure.Services.ICurrentUser;
using laundryghar.Engagement.Application.Cms.Commands;
using laundryghar.Engagement.Application.Cms.Dtos;
using laundryghar.Engagement.Application.Cms.Queries;
using MediatR;

namespace laundryghar.Engagement.Endpoints;

public static class AdminNotificationTemplateEndpoints
{
    public static RouteGroupBuilder MapAdminNotificationTemplateEndpoints(this RouteGroupBuilder group)
    {
        var g = group.MapGroup("/notification-templates")
            .WithTags("Admin - CMS - Notification Templates");

        g.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? status = null) =>
        {
            var r = await sender.Send(new GetNotificationTemplatesQuery(
                page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status), ct);
            return Results.Ok(new PaginatedListResponse<NotificationTemplateDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cms.template.manage");

        g.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetNotificationTemplateByIdQuery(id), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<NotificationTemplateDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cms.template.manage");

        g.MapPost("/", async (CreateNotificationTemplateRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateNotificationTemplateCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/notification-templates/{r.Id}",
                new SingleResponse<NotificationTemplateDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cms.template.manage");

        g.MapPut("/{id:guid}", async (Guid id, UpdateNotificationTemplateRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateNotificationTemplateCommand(id, req, u.UserId), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<NotificationTemplateDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cms.template.manage");

        g.MapDelete("/{id:guid}", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new DeleteNotificationTemplateCommand(id, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:cms.template.manage");

        return group;
    }
}
