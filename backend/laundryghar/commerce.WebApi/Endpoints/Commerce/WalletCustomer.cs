using commerce.Application.Commerce;
using commerce.Application.Commerce.Customer.Wallet;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace commerce.WebApi.Endpoints.Commerce;

/// <summary>Customer — wallet: read, transactions, top-up initiate/verify (CustomerOnly).</summary>
public class WalletCustomer : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/customer/wallet";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Customer - Wallet");
        group.RequireAuthorization("CustomerOnly");

        group.MapGet(GetWallet, "/");
        group.MapGet(GetTransactions, "/transactions");
        group.MapPost(TopUpInitiate, "/topup/initiate");
        group.MapPost(TopUpVerify, "/topup/verify");
    }

    public static async Task<IResult> GetWallet(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (u.UserId is not { } customerId) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetMyWalletQuery(customerId, u.BrandId ?? Guid.Empty), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<WalletAccountDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetTransactions(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20)
    {
        if (u.UserId is not { } customerId) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetMyWalletTransactionsQuery(
            customerId, u.BrandId ?? Guid.Empty, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<WalletTransactionDto> { Status = true, Data = r });
    }

    public static async Task<IResult> TopUpInitiate(
        HttpContext http, WalletTopUpRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (u.UserId is not { } customerId) return Results.Unauthorized();
        var brandId = u.BrandId ?? Guid.Empty;
        var idempotencyKey = PackagesCustomer.GetIdempotencyKey(http) ?? $"topup_{customerId}_{Guid.NewGuid():N}";
        var r = await dispatcher.SendAsync(new WalletTopUpInitiateCommand(customerId, brandId, req, idempotencyKey), ct);
        return Results.Ok(new SingleResponse<PaymentDto> { Status = true, Data = r });
    }

    public static async Task<IResult> TopUpVerify(
        VerifyPaymentRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (u.UserId is not { } customerId) return Results.Unauthorized();
        var r = await dispatcher.SendAsync(new WalletTopUpVerifyCommand(customerId, u.BrandId ?? Guid.Empty, req), ct);
        return Results.Ok(new SingleResponse<WalletTransactionDto> { Status = true, Data = r });
    }
}
