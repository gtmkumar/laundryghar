using commerce.Application.Commerce;
using commerce.Application.Commerce.Admin.Subscriptions;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;

namespace commerce.WebApi.Endpoints.Commerce;

/// <summary>Admin — customer subscriptions: read (permission:subscription.read) +
/// narrow status patch with optimistic concurrency (permission:subscription.manage).</summary>
public class CustomerSubscriptionsAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/subscriptions";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Commerce - Customer Subscriptions");
        group.RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:subscription.read");
        group.MapPatch(PatchStatus, "/{id:guid}/status")
            .AddEndpointFilter<ValidationFilter<PatchCustomerSubscriptionStatusRequest>>()
            .RequireAuthorization("permission:subscription.manage");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, Guid? customerId = null, string? status = null)
    {
        var r = await dispatcher.QueryAsync(new GetCustomerSubscriptionsAdminQuery(
            page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, customerId, status), ct);
        return Results.Ok(new PaginatedListResponse<CustomerSubscriptionDto> { Status = true, Data = r });
    }

    // PATCH /admin/subscriptions/{id}/status — narrow status update with optimistic concurrency.
    public static async Task<IResult> PatchStatus(
        Guid id, PatchCustomerSubscriptionStatusRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(
            new PatchCustomerSubscriptionStatusCommand(id, req.Status, req.ExpectedUpdatedAt, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<CustomerSubscriptionDto> { Status = true, Data = r });
    }
}
