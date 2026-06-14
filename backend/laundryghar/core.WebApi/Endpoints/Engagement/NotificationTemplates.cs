using core.Application.NotificationTemplates.Commands.CreateNotificationTemplate;
using core.Application.NotificationTemplates.Queries.GetNotificationTemplateByCode;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace core.WebApi.Endpoints.Engagement;

/// <summary>Notification template endpoints. Thin: dispatch command/query via <see cref="IDispatcher"/>.</summary>
public class NotificationTemplates : IEndpointGroup
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost(Create).AddEndpointFilter<ValidationFilter<CreateNotificationTemplateCommand>>();
        group.MapGet(GetByCode, "{brandId:guid}/{code}");
    }

    public static async Task<Results<Created<Guid>, BadRequest>> Create(
        IDispatcher dispatcher, CreateNotificationTemplateCommand command, CancellationToken ct)
    {
        var result = await dispatcher.SendAsync(command, ct);
        return result.HasSuccess
            ? TypedResults.Created($"/api/NotificationTemplates/{result.UserObject}", result.UserObject)
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
