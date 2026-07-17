using commerce.Application.Commerce;
using commerce.Application.Commerce.Admin.Subscriptions;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Caching;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;

namespace commerce.WebApi.Endpoints.Commerce;

/// <summary>Admin — subscription plans. All routes gated by permission:subscription.manage.</summary>
public class SubscriptionPlansAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/subscription-plans";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Commerce - Subscription Plans");
        group.RequireAuthorization();
        // Plan create/update/delete/status writes regenerate the cached customer plan listing.
        group.EvictOutputCacheOnWrite(CommerceCacheTags.Plans);

        group.MapGet(GetAll, "/").RequireAuthorization("permission:subscription.manage");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:subscription.manage");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateSubscriptionPlanRequest>>()
            .RequireAuthorization("permission:subscription.manage");
        group.MapPut(Update, "/{id:guid}")
            .AddEndpointFilter<ValidationFilter<UpdateSubscriptionPlanRequest>>()
            .RequireAuthorization("permission:subscription.manage");
        group.MapDelete(Delete, "/{id:guid}").RequireAuthorization("permission:subscription.manage");
        group.MapPatch(PatchStatus, "/{id:guid}/status")
            .AddEndpointFilter<ValidationFilter<PatchSubscriptionPlanStatusRequest>>()
            .RequireAuthorization("permission:subscription.manage");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var r = await dispatcher.QueryAsync(new GetSubscriptionPlansQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<SubscriptionPlanDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetSubscriptionPlanByIdQuery(id), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<SubscriptionPlanDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreateSubscriptionPlanRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateSubscriptionPlanCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/subscription-plans/{r.Id}", new SingleResponse<SubscriptionPlanDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdateSubscriptionPlanRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdateSubscriptionPlanCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<SubscriptionPlanDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteSubscriptionPlanCommand(id, u.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    public static async Task<IResult> PatchStatus(Guid id, PatchSubscriptionPlanStatusRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new PatchSubscriptionPlanStatusCommand(id, req.Status, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<SubscriptionPlanDto> { Status = true, Data = r });
    }
}
