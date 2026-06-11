using laundryghar.Engagement.Application.Cms.Commands;
using laundryghar.Engagement.Application.Cms.Dtos;
using laundryghar.Engagement.Application.Cms.Queries;
using MediatR;

namespace laundryghar.Engagement.Endpoints;

public static class AdminNotificationLogEndpoints
{
    public static RouteGroupBuilder MapAdminNotificationLogEndpoints(this RouteGroupBuilder group)
    {
        // ── Outbox ──────────────────────────────────────────────────────────────
        var outbox = group.MapGroup("/notification-outbox")
            .WithTags("Admin - CMS - Notification Outbox");

        outbox.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? status = null) =>
        {
            var r = await sender.Send(new GetNotificationOutboxQuery(
                page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status), ct);
            return Results.Ok(new PaginatedListResponse<NotificationOutboxDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cms.notification.read");

        outbox.MapPost("/{id:guid}/retry", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new RetryNotificationOutboxCommand(id, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:cms.notification.manage");

        // ── Notification Log ────────────────────────────────────────────────────
        var logs = group.MapGroup("/notification-logs")
            .WithTags("Admin - CMS - Notification Logs");

        logs.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? channel = null) =>
        {
            var r = await sender.Send(new GetNotificationLogsQuery(
                page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, channel), ct);
            return Results.Ok(new PaginatedListResponse<NotificationLogDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cms.notification.read");

        // ── WhatsApp Message Log ────────────────────────────────────────────────
        var waLogs = group.MapGroup("/whatsapp-logs")
            .WithTags("Admin - CMS - WhatsApp Message Logs");

        waLogs.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? direction = null) =>
        {
            var r = await sender.Send(new GetWhatsAppLogsQuery(
                page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, direction), ct);
            return Results.Ok(new PaginatedListResponse<WhatsAppMessageLogDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cms.notification.read");

        return group;
    }
}
