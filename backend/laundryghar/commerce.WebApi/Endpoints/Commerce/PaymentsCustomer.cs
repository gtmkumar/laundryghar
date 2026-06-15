using commerce.Application.Commerce;
using commerce.Application.Commerce.Customer.Payments;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace commerce.WebApi.Endpoints.Commerce;

/// <summary>Customer — general-purpose payment initiate/verify (CustomerOnly).</summary>
public class PaymentsCustomer : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/customer/payments";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Customer - Payments");
        group.RequireAuthorization("CustomerOnly");

        group.MapPost(Initiate, "/initiate");
        group.MapPost(Verify, "/verify");
    }

    public static async Task<IResult> Initiate(
        HttpContext http, InitiatePaymentRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (u.UserId is not { } customerId) return Results.Unauthorized();
        var brandId = u.BrandId ?? Guid.Empty;
        var idempotencyKey = PackagesCustomer.GetIdempotencyKey(http) ?? $"pay_{customerId}_{Guid.NewGuid():N}";
        var r = await dispatcher.SendAsync(new InitiatePaymentCommand(customerId, brandId, req, idempotencyKey), ct);
        return Results.Ok(new SingleResponse<PaymentDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Verify(
        VerifyPaymentRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (u.UserId is not { } customerId) return Results.Unauthorized();
        var r = await dispatcher.SendAsync(new VerifyPaymentCommand(customerId, u.BrandId ?? Guid.Empty, req), ct);
        return Results.Ok(new SingleResponse<PaymentDto> { Status = true, Data = r });
    }
}
