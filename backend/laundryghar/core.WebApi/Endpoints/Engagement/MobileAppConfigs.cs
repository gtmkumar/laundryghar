using core.Application.Engagement.Cms.MobileAppConfigs.Commands.CreateMobileAppConfig;
using core.Application.Engagement.Cms.MobileAppConfigs.Commands.DeleteMobileAppConfig;
using core.Application.Engagement.Cms.MobileAppConfigs.Commands.UpdateMobileAppConfig;
using core.Application.Engagement.Cms.MobileAppConfigs.Queries.GetMobileAppConfigById;
using core.Application.Engagement.Cms.MobileAppConfigs.Queries.GetMobileAppConfigs;
using core.Application.Engagement.Cms.Dtos;
using laundryghar.Utilities.Validation;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace core.WebApi.Endpoints.Engagement;

/// <summary>
/// Admin CMS — Mobile App Config endpoints. Thin: each method dispatches a command/query through
/// <see cref="IDispatcher"/>. No business logic here.
/// </summary>
public class MobileAppConfigs : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/app-config";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - CMS - Mobile App Config")
             .RequireAuthorization("permission:cms.appconfig.manage");

        group.MapGet(GetAll);
        group.MapGet(GetById, "{id:guid}");
        group.MapPost(Create).AddEndpointFilter<ValidationFilter<CreateMobileAppConfigRequest>>();
        group.MapPut(Update, "{id:guid}").AddEndpointFilter<ValidationFilter<UpdateMobileAppConfigRequest>>();
        group.MapDelete(Delete, "{id:guid}");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, string? platform = null)
    {
        var data = await dispatcher.QueryAsync(
            new GetMobileAppConfigsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, platform), ct);
        return Results.Ok(new PaginatedListResponse<MobileAppConfigDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetMobileAppConfigByIdQuery(id), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<MobileAppConfigDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Create(CreateMobileAppConfigRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CreateMobileAppConfigCommand(req, user.UserId), ct);
        return Results.Created($"/api/v1/admin/app-config/{data.Id}",
            new SingleResponse<MobileAppConfigDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Update(Guid id, UpdateMobileAppConfigRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new UpdateMobileAppConfigCommand(id, req, user.UserId), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<MobileAppConfigDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteMobileAppConfigCommand(id, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
