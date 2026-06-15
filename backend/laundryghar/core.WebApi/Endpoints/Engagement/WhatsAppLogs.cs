using core.Application.Engagement.Cms.Dtos;
using core.Application.Engagement.Cms.WhatsAppLogs.Queries.GetWhatsAppLogs;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Endpoints;

namespace core.WebApi.Endpoints.Engagement;

/// <summary>
/// Admin CMS — WhatsApp message log endpoints. Read-only listing scoped to the caller's brand.
/// </summary>
public class WhatsAppLogs : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/whatsapp-logs";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - CMS - WhatsApp Message Logs")
             .RequireAuthorization("permission:cms.notification.read");

        group.MapGet(GetAll);
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, string? direction = null)
    {
        var data = await dispatcher.QueryAsync(
            new GetWhatsAppLogsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, direction), ct);
        return Results.Ok(new PaginatedListResponse<WhatsAppMessageLogDto> { Status = true, Data = data });
    }
}
