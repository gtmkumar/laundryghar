using core.Application.Engagement.Cms.Dtos;
using core.Application.Engagement.Cms.NotificationLogs.Queries.GetNotificationLogs;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Endpoints;

namespace core.WebApi.Endpoints.Engagement;

/// <summary>
/// Admin CMS — Notification delivery log endpoints. Read-only listing scoped to the caller's brand.
/// </summary>
public class NotificationLogs : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/notification-logs";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - CMS - Notification Logs")
             .RequireAuthorization("permission:cms.notification.read");

        group.MapGet(GetAll);
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, string? channel = null)
    {
        var data = await dispatcher.QueryAsync(
            new GetNotificationLogsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, channel), ct);
        return Results.Ok(new PaginatedListResponse<NotificationLogDto> { Status = true, Data = data });
    }
}
