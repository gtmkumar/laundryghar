using commerce.Application.Commerce;
using commerce.Application.Commerce.Admin.Wallet;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;

namespace commerce.WebApi.Endpoints.Commerce;

/// <summary>Admin — customer wallets: read (permission:wallet.read), adjust (permission:wallet.adjust).</summary>
public class WalletsAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/wallets";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Commerce - Wallets");
        group.RequireAuthorization();

        group.MapGet(GetWallet, "/{customerId:guid}").RequireAuthorization("permission:wallet.read");
        group.MapGet(GetTransactions, "/{customerId:guid}/transactions").RequireAuthorization("permission:wallet.read");
        group.MapPost(Adjust, "/adjust")
            .AddEndpointFilter<ValidationFilter<AdminWalletAdjustRequest>>()
            .RequireAuthorization("permission:wallet.adjust");
    }

    public static async Task<IResult> GetWallet(Guid customerId, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetCustomerWalletQuery(customerId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<WalletAccountDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetTransactions(Guid customerId, IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20)
    {
        var r = await dispatcher.QueryAsync(new GetCustomerWalletTransactionsQuery(customerId, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<WalletTransactionDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Adjust(AdminWalletAdjustRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new AdminWalletAdjustCommand(req, u.UserId), ct);
        return Results.Ok(new SingleResponse<WalletTransactionDto> { Status = true, Data = r });
    }
}
