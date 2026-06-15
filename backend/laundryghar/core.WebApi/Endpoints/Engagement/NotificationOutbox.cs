using core.Application.Engagement.Cms.Dtos;
using core.Application.Engagement.Cms.NotificationOutbox.Commands.RetryNotificationOutbox;
using core.Application.Engagement.Cms.NotificationOutbox.Queries.GetNotificationOutbox;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace core.WebApi.Endpoints.Engagement;

/// <summary>
/// Admin CMS — Notification Outbox endpoints. Read-only log listing plus a retry command for
/// failed entries. Thin: each method dispatches a query/command through <see cref="IDispatcher"/>.
/// </summary>
public class NotificationOutbox : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/notification-outbox";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - CMS - Notification Outbox");

        group.MapGet(GetAll).RequireAuthorization("permission:cms.notification.read");
        group.MapPost(Retry, "{id:guid}/retry").RequireAuthorization("permission:cms.notification.manage");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, string? status = null)
    {
        var data = await dispatcher.QueryAsync(
            new GetNotificationOutboxQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status), ct);
        return Results.Ok(new PaginatedListResponse<NotificationOutboxDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Retry(Guid id, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new RetryNotificationOutboxCommand(id, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
