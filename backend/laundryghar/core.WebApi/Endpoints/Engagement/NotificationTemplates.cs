using core.Application.NotificationTemplates.Commands.CreateNotificationTemplate;
using core.Application.NotificationTemplates.Queries.GetNotificationTemplateByCode;
using core.Application.NotificationTemplates.Queries.GetNotificationTemplates;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace core.WebApi.Endpoints.Engagement;

/// <summary>Admin CMS — Notification template endpoints. Thin: dispatch command/query via
/// <see cref="IDispatcher"/>. Reads gated by <c>cms.notification.read</c>, writes by
/// <c>cms.notification.manage</c> (mirrors <see cref="NotificationOutbox"/>).</summary>
public class NotificationTemplates : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/notification-templates";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - CMS - Notification Templates");

        group.MapGet(List).RequireAuthorization("permission:cms.notification.read");
        group.MapGet(GetByCode, "{brandId:guid}/{code}").RequireAuthorization("permission:cms.notification.read");
        group.MapPost(Create)
             .AddEndpointFilter<ValidationFilter<CreateNotificationTemplateCommand>>()
             .RequireAuthorization("permission:cms.notification.manage");
    }

    public static async Task<IResult> List(
        ICurrentUser user, IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var data = await dispatcher.QueryAsync(
            new GetNotificationTemplatesQuery(user.RequireBrandId(), page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<NotificationTemplateDto> { Status = true, Data = data });
    }

    public static async Task<Results<Created<Guid>, BadRequest>> Create(
        IDispatcher dispatcher, CreateNotificationTemplateCommand command, CancellationToken ct)
    {
        var result = await dispatcher.SendAsync(command, ct);
        return result.HasSuccess
            ? TypedResults.Created($"/api/v1/admin/notification-templates/{result.UserObject}", result.UserObject)
            : TypedResults.BadRequest();
    }

    public static async Task<Results<Ok<NotificationTemplateDto>, NotFound>> GetByCode(
        IDispatcher dispatcher, Guid brandId, string code, CancellationToken ct)
    {
        var result = await dispatcher.QueryAsync(new GetNotificationTemplateByCodeQuery(brandId, code), ct);
        return result.HasSuccess
            ? TypedResults.Ok(result.UserObject!)
            : TypedResults.NotFound();
    }
}
