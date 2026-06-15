using commerce.Application.Commerce;
using commerce.Application.Commerce.Customer.Loyalty;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace commerce.WebApi.Endpoints.Commerce;

/// <summary>Customer — loyalty balance + history (CustomerOnly).</summary>
public class LoyaltyCustomer : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/customer/loyalty";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Customer - Loyalty");
        group.RequireAuthorization("CustomerOnly");

        group.MapGet(GetBalance, "/balance");
    }

    public static async Task<IResult> GetBalance(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (u.UserId is not { } customerId) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetMyLoyaltyBalanceQuery(customerId, u.BrandId ?? Guid.Empty), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<LoyaltyBalanceDto> { Status = true, Data = r });
    }
}
