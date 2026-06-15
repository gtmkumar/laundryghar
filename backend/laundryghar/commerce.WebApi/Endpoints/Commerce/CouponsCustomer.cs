using commerce.Application.Commerce;
using commerce.Application.Commerce.Customer.Coupons;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace commerce.WebApi.Endpoints.Commerce;

/// <summary>Customer — coupons: list applicable + validate/apply (CustomerOnly).</summary>
public class CouponsCustomer : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/customer/coupons";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Customer - Coupons");
        group.RequireAuthorization("CustomerOnly");

        group.MapGet(GetApplicable, "/");
        group.MapPost(ValidateApply, "/validate-apply");
    }

    public static async Task<IResult> GetApplicable(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (u.UserId is not { } customerId) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetApplicableCouponsQuery(customerId, u.BrandId ?? Guid.Empty), ct);
        return Results.Ok(new ListResponse<CouponDto> { Status = true, Data = r });
    }

    public static async Task<IResult> ValidateApply(
        ValidateCouponRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (u.UserId is not { } customerId) return Results.Unauthorized();
        var r = await dispatcher.SendAsync(new ValidateApplyCouponCommand(customerId, u.BrandId ?? Guid.Empty, req), ct);
        return Results.Ok(new SingleResponse<CouponRedemptionDto> { Status = true, Data = r });
    }
}
