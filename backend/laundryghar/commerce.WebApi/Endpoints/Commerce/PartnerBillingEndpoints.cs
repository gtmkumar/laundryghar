using commerce.Application.Commerce.Partner.Wallet;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;

namespace commerce.WebApi.Endpoints.Commerce;

/// <summary>
/// RaaS partner billing lane — prepaid wallet (/api/v1/partner/wallet*, FULL-9 / issue #14).
/// Group-gated by the "PartnerOnly" policy (token_use=partner); isolation is by partner_id — the
/// RLS interceptor sets app.current_partner_id from the token, so rls_partner scopes every read
/// and the top-up WITH CHECK to the caller's own partner.
///
/// GET  /api/v1/partner/wallet               (PartnerOnly — both roles read balance)
/// GET  /api/v1/partner/wallet/transactions  (PartnerAdmin — §13 operator can't see the ledger)
/// POST /api/v1/partner/wallet/top-up        (PartnerAdmin — manual/prepaid credit)
/// </summary>
public class PartnerBillingEndpoints : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/partner";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Partner - Wallet").RequireAuthorization("PartnerOnly");

        // Balance read stays open to both partner roles.
        group.MapGet(GetWallet, "/wallet");

        // Billing ledger + money-movement are PartnerAdmin-only (docs/rbac.md §13).
        group.MapGet(GetTransactions, "/wallet/transactions")
            .RequireAuthorization("PartnerAdmin");

        group.MapPost(TopUp, "/wallet/top-up")
            .RequireAuthorization("PartnerAdmin")
            .AddEndpointFilter<ValidationFilter<TopUpPartnerWalletRequest>>();
    }

    public static async Task<IResult> GetWallet(IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetPartnerWalletQuery(), ct);
        return Results.Ok(new SingleResponse<PartnerWalletDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetTransactions(
        IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var r = await dispatcher.QueryAsync(
            new GetPartnerWalletTransactionsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<PartnerWalletTransactionDto> { Status = true, Data = r });
    }

    public static async Task<IResult> TopUp(
        TopUpPartnerWalletRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        // sub = partner_user_id for a partner token; PartnerAdmin guarantees it is present.
        var actorId = u.UserId is { } uid && uid != Guid.Empty ? uid : (Guid?)null;
        var r = await dispatcher.SendAsync(new TopUpPartnerWalletCommand(req, actorId), ct);
        return Results.Ok(new SingleResponse<PartnerWalletTransactionDto> { Status = true, Data = r });
    }
}
