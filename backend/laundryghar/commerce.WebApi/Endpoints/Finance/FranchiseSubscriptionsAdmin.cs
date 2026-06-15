using commerce.Application.Finance.Subscriptions.Commands;
using commerce.Application.Finance.Subscriptions.Dtos;
using commerce.Application.Finance.Subscriptions.Queries;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;

namespace commerce.WebApi.Endpoints.Finance;

/// <summary>Admin — SaaS franchise subscriptions (platform_admin only). Reads gated by saas.read;
/// mutations by saas.manage.</summary>
public class FranchiseSubscriptionsAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/franchise-subscriptions";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - SaaS - Franchise Subscriptions");
        group.RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:saas.read");
        group.MapPost(Assign, "/assign")
            .AddEndpointFilter<ValidationFilter<AssignFranchisePlanRequest>>()
            .RequireAuthorization("permission:saas.manage");
        group.MapPost(Cancel, "/{id:guid}/cancel").RequireAuthorization("permission:saas.manage");
    }

    public static async Task<IResult> GetAll(
        IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, Guid? franchiseId = null, string? status = null)
    {
        var r = await dispatcher.QueryAsync(new GetFranchiseSubscriptionsQuery(
            page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, franchiseId, status), ct);
        return Results.Ok(new PaginatedListResponse<FranchiseSubscriptionDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Assign(
        AssignFranchisePlanRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new AssignFranchisePlanCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/franchise-subscriptions/{r.Id}",
            new SingleResponse<FranchiseSubscriptionDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Cancel(
        Guid id, CancelFranchiseSubscriptionRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CancelFranchiseSubscriptionCommand(id, req.Reason, u.UserId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<FranchiseSubscriptionDto> { Status = true, Data = r });
    }
}
