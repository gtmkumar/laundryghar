using commerce.Application.Commerce;
using commerce.Application.Commerce.Customer.Subscriptions;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace commerce.WebApi.Endpoints.Commerce;

/// <summary>Customer — subscriptions: plans, my subscriptions, subscribe, cancel (CustomerOnly).</summary>
public class SubscriptionsCustomer : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/customer/subscriptions";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Customer - Subscriptions");
        group.RequireAuthorization("CustomerOnly");

        group.MapGet(GetPlans, "/plans");
        group.MapGet(GetMine, "/");
        group.MapPost(Subscribe, "/");
        group.MapPost(Cancel, "/{id:guid}/cancel");
    }

    // GET /customer/subscriptions/plans — active, public plans for the customer's brand
    public static async Task<IResult> GetPlans(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (u.UserId is not { } customerId) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetActiveSubscriptionPlansQuery(customerId, u.BrandId ?? Guid.Empty), ct);
        return Results.Ok(new ListResponse<SubscriptionPlanDto> { Status = true, Data = r });
    }

    // GET /customer/subscriptions — list my subscriptions
    public static async Task<IResult> GetMine(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (u.UserId is not { } customerId) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetMySubscriptionsQuery(customerId, u.BrandId ?? Guid.Empty), ct);
        return Results.Ok(new ListResponse<CustomerSubscriptionDto> { Status = true, Data = r });
    }

    // POST /customer/subscriptions — subscribe (creates mandate + subscription)
    public static async Task<IResult> Subscribe(
        SubscribeRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (u.UserId is not { } customerId) return Results.Unauthorized();
        var r = await dispatcher.SendAsync(new SubscribeCommand(customerId, u.BrandId ?? Guid.Empty, req), ct);
        return Results.Created($"/api/v1/customer/subscriptions/{r.Id}",
            new SingleResponse<CustomerSubscriptionDto> { Status = true, Data = r });
    }

    // POST /customer/subscriptions/{id}/cancel — end-of-period cancel
    public static async Task<IResult> Cancel(
        Guid id, CancelSubscriptionRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (u.UserId is not { } customerId) return Results.Unauthorized();
        var r = await dispatcher.SendAsync(new CancelSubscriptionCommand(id, customerId, u.BrandId ?? Guid.Empty, req.Reason), ct);
        return r is null ? Results.NotFound()
            : Results.Ok(new SingleResponse<CustomerSubscriptionDto> { Status = true, Data = r });
    }
}
