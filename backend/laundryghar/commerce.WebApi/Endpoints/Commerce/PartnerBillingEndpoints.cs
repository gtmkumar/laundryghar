using commerce.Application.Commerce.Partner.Invoices;
using commerce.Application.Commerce.Partner.Wallet;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;

namespace commerce.WebApi.Endpoints.Commerce;

/// <summary>
/// RaaS partner billing lane — prepaid wallet + invoices (/api/v1/partner/*, FULL-9 + FULL-10 / issue #14).
/// Group-gated by the "PartnerOnly" policy (token_use=partner); isolation is by partner_id — the RLS
/// interceptor sets app.current_partner_id from the token, so rls_partner scopes every read + write to
/// the caller's own partner. Billing + money-movement routes are additionally PartnerAdmin-only
/// (docs/rbac.md §13: a partner operator must not see or move billing).
///
/// Wallet:
///   GET  /partner/wallet                             (PartnerOnly — both roles read balance)
///   GET  /partner/wallet/transactions                (PartnerAdmin — ledger)
///   POST /partner/wallet/top-up                      (PartnerAdmin — direct/manual credit)
///   POST /partner/wallet/top-up/link                 (PartnerAdmin — Razorpay-backed credit)
///   POST /partner/wallet/top-up/link/{linkId}/sync   (PartnerAdmin — pull reconcile → credit)
/// Invoices (all PartnerAdmin):
///   GET  /partner/invoices                           (paged list)
///   GET  /partner/invoices/{id}                      (one invoice)
///   GET  /partner/invoices/{id}/pdf                  (stored PDF url; 404 stub if none)
///   POST /partner/invoices/{id}/pay                  (create Razorpay payment link for AmountDue)
///   POST /partner/invoices/{id}/sync                 (pull reconcile → mark paid)
/// </summary>
public class PartnerBillingEndpoints : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/partner";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Partner - Billing").RequireAuthorization("PartnerOnly");

        // ── Wallet ────────────────────────────────────────────────────────────────
        // Balance read stays open to both partner roles.
        group.MapGet(GetWallet, "/wallet");

        // Billing ledger + money-movement are PartnerAdmin-only (docs/rbac.md §13).
        group.MapGet(GetTransactions, "/wallet/transactions")
            .RequireAuthorization("PartnerAdmin");

        group.MapPost(TopUp, "/wallet/top-up")
            .RequireAuthorization("PartnerAdmin")
            .AddEndpointFilter<ValidationFilter<TopUpPartnerWalletRequest>>();

        group.MapPost(TopUpViaLink, "/wallet/top-up/link")
            .RequireAuthorization("PartnerAdmin")
            .AddEndpointFilter<ValidationFilter<TopUpPartnerWalletViaLinkRequest>>();

        group.MapPost(SyncTopUp, "/wallet/top-up/link/{linkId}/sync")
            .RequireAuthorization("PartnerAdmin");

        // ── Invoices (all PartnerAdmin) ─────────────────────────────────────────────
        group.MapGet(GetInvoices, "/invoices").RequireAuthorization("PartnerAdmin");
        group.MapGet(GetInvoice, "/invoices/{id:guid}").RequireAuthorization("PartnerAdmin");
        group.MapGet(GetInvoicePdf, "/invoices/{id:guid}/pdf").RequireAuthorization("PartnerAdmin");
        group.MapPost(PayInvoice, "/invoices/{id:guid}/pay").RequireAuthorization("PartnerAdmin");
        group.MapPost(SyncInvoice, "/invoices/{id:guid}/sync").RequireAuthorization("PartnerAdmin");
    }

    // ── Wallet handlers ─────────────────────────────────────────────────────────────

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

    public static async Task<IResult> TopUpViaLink(
        TopUpPartnerWalletViaLinkRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var actorId = u.UserId is { } uid && uid != Guid.Empty ? uid : (Guid?)null;
        var r = await dispatcher.SendAsync(new TopUpPartnerWalletViaLinkCommand(req, actorId), ct);
        return Results.Ok(new SingleResponse<TopUpPartnerWalletViaLinkResponse> { Status = true, Data = r });
    }

    public static async Task<IResult> SyncTopUp(
        string linkId, TopUpSyncBody body, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new SyncPartnerWalletTopUpCommand(linkId, body.IdempotencyKey), ct);
        return Results.Ok(new SingleResponse<SyncPartnerWalletTopUpResponse> { Status = true, Data = r });
    }

    /// <summary>Body for the wallet top-up sync — the idempotency key the link was created with.</summary>
    public sealed record TopUpSyncBody(string IdempotencyKey);

    // ── Invoice handlers ────────────────────────────────────────────────────────────

    public static async Task<IResult> GetInvoices(
        IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var r = await dispatcher.QueryAsync(
            new GetPartnerInvoicesQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<PartnerInvoiceListItemDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetInvoice(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetPartnerInvoiceByIdQuery(id), ct);
        return r is null
            ? Results.NotFound(new SingleResponse<PartnerInvoiceDto> { Status = false })
            : Results.Ok(new SingleResponse<PartnerInvoiceDto> { Status = true, Data = r });
    }

    /// <summary>Returns the stored invoice PDF url. This wave does NOT render PDFs — a 404 is returned
    /// when no PDF has been produced yet (or the invoice is not the caller's).</summary>
    public static async Task<IResult> GetInvoicePdf(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var url = await dispatcher.QueryAsync(new GetPartnerInvoicePdfUrlQuery(id), ct);
        return string.IsNullOrEmpty(url)
            ? Results.NotFound(new SingleResponse<string> { Status = false })
            : Results.Ok(new SingleResponse<string> { Status = true, Data = url });
    }

    public static async Task<IResult> PayInvoice(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new PayPartnerInvoiceCommand(id), ct);
        return Results.Ok(new SingleResponse<PayPartnerInvoiceResponse> { Status = true, Data = r });
    }

    public static async Task<IResult> SyncInvoice(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var status = await dispatcher.SendAsync(new SyncPartnerInvoicePaymentCommand(id), ct);
        return Results.Ok(new SingleResponse<string?> { Status = true, Data = status });
    }
}
