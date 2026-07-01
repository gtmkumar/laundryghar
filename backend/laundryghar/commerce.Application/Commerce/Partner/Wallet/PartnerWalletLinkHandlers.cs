using commerce.Application.Common.Interfaces;
using FluentValidation;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.Exceptions;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace commerce.Application.Commerce.Partner.Wallet;

// ── Top-up via Razorpay payment link (PartnerAdmin) ─────────────────────────────

/// <summary>
/// Gateway-backed prepaid top-up. Creates a Razorpay payment link for the amount and returns the
/// payable URL — it does NOT credit the wallet. The credit is applied later, idempotently, when the
/// payment is confirmed: either by the partner paylink webhook (push) or
/// <see cref="SyncPartnerWalletTopUpCommand"/> (pull). Both apply the credit through the SAME shared
/// <see cref="PartnerWalletLedger"/> primitive keyed by <see cref="TopUpPartnerWalletViaLinkRequest.IdempotencyKey"/>,
/// so at most one credit lands regardless of how many confirmation signals arrive. The existing
/// direct-credit <see cref="TopUpPartnerWalletCommand"/> is unchanged (kept for dev/manual credits).
/// </summary>
public sealed record TopUpPartnerWalletViaLinkCommand(TopUpPartnerWalletViaLinkRequest Request, Guid? ActorId)
    : ICommand<TopUpPartnerWalletViaLinkResponse>;

public sealed class TopUpPartnerWalletViaLinkHandler
    : ICommandHandler<TopUpPartnerWalletViaLinkCommand, TopUpPartnerWalletViaLinkResponse>
{
    private readonly ICurrentTenant _tenant;
    private readonly IPartnerPaymentLinkClient _rzp;

    public TopUpPartnerWalletViaLinkHandler(ICurrentTenant tenant, IPartnerPaymentLinkClient rzp)
    {
        _tenant = tenant;
        _rzp = rzp;
    }

    public async Task<TopUpPartnerWalletViaLinkResponse> HandleAsync(
        TopUpPartnerWalletViaLinkCommand cmd, CancellationToken ct)
    {
        var partnerId = _tenant.PartnerId
            ?? throw new UnauthorizedAccessException("Partner context required.");

        var req = cmd.Request;
        if (req.Amount <= 0)
            throw new BusinessRuleException("Top-up amount must be > 0.");
        if (string.IsNullOrWhiteSpace(req.IdempotencyKey))
            throw new BusinessRuleException("An idempotency key is required for a top-up.");

        if (!await _rzp.IsConfiguredAsync(ct))
            throw new BusinessRuleException(
                "Razorpay is not configured. Enable it under Settings → Platform billing, or set Razorpay:KeyId / Razorpay:KeySecret.");

        // A fresh reference id per link (Razorpay requires reference_id unique); the idempotency key
        // rides in notes so the CREDIT — not the link — is what's deduplicated on confirmation.
        var referenceId = Guid.NewGuid().ToString();
        var link = await _rzp.CreatePaymentLinkAsync(
            req.Amount, PartnerWalletMap.DefaultCurrency, "Partner wallet top-up", referenceId,
            notes: new Dictionary<string, string>
            {
                ["kind"] = "partner_wallet_topup",
                ["partner_id"] = partnerId.ToString(),
                ["idempotency_key"] = req.IdempotencyKey,
            }, ct);

        return new TopUpPartnerWalletViaLinkResponse(link.Id, link.ShortUrl, req.IdempotencyKey);
    }
}

public sealed class TopUpPartnerWalletViaLinkValidator : AbstractValidator<TopUpPartnerWalletViaLinkRequest>
{
    public TopUpPartnerWalletViaLinkValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100);
    }
}

// ── Sync a top-up link against Razorpay (pull reconcile → credit) ────────────────

/// <summary>
/// Pull the top-up link's status from Razorpay; if paid, credit the wallet ONCE via the shared ledger
/// primitive. Security-critical: the credit is bound to SERVER-authored state, never to caller input.
///
/// The link was created on the shared PLATFORM Razorpay account by
/// <see cref="TopUpPartnerWalletViaLinkHandler"/>, which stamped its <c>notes</c> with
/// <c>kind=partner_wallet_topup</c>, <c>partner_id</c> and <c>idempotency_key</c>. Razorpay echoes those
/// notes back verbatim on fetch, so this handler mirrors the safe webhook
/// (<c>ProcessPartnerPaylinkWebhookHandler</c>): it VERIFIES the link is a wallet top-up AND that
/// <c>notes.partner_id</c> equals the CALLING partner (rejecting invoices, brand links and other
/// partners' links), then credits Razorpay's authoritative <c>amount_paid</c> keyed on the
/// <c>notes.idempotency_key</c>. Because that key is fixed per link, a repeat sync re-uses the SAME key
/// and the ledger dedups it — the link can be credited at most once, and the webhook converges on the
/// same key. The caller supplies ONLY the link id; no client-supplied idempotency key is trusted.
/// </summary>
public sealed record SyncPartnerWalletTopUpCommand(string LinkId)
    : ICommand<SyncPartnerWalletTopUpResponse>;

public sealed class SyncPartnerWalletTopUpHandler
    : ICommandHandler<SyncPartnerWalletTopUpCommand, SyncPartnerWalletTopUpResponse>
{
    private const string TopUpKind = "partner_wallet_topup";

    private readonly ICommerceDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly IPartnerPaymentLinkClient _rzp;

    public SyncPartnerWalletTopUpHandler(ICommerceDbContext db, ICurrentTenant tenant, IPartnerPaymentLinkClient rzp)
    {
        _db = db;
        _tenant = tenant;
        _rzp = rzp;
    }

    public async Task<SyncPartnerWalletTopUpResponse> HandleAsync(
        SyncPartnerWalletTopUpCommand cmd, CancellationToken ct)
    {
        var partnerId = _tenant.PartnerId
            ?? throw new UnauthorizedAccessException("Partner context required.");
        if (string.IsNullOrWhiteSpace(cmd.LinkId))
            throw new BusinessRuleException("A payment link id is required.");

        var details = await _rzp.GetPaymentLinkAsync(cmd.LinkId, ct);
        if (!string.Equals(details.Status, "paid", StringComparison.OrdinalIgnoreCase))
            return new SyncPartnerWalletTopUpResponse(details.Status, Transaction: null);

        // ── Bind the credit to the link's SERVER-set notes, never to caller input ──
        var notes = details.Notes;

        // 1. Must be a partner wallet top-up link (rejects partner_invoice / brand / unrelated links).
        if (!notes.TryGetValue("kind", out var kind) || kind != TopUpKind)
            throw new BusinessRuleException("This payment link is not a partner wallet top-up.");

        // 2. Must belong to the CALLING partner — a paid link from another partner (or an invoice link
        //    that happens to be paid) must never credit this caller's wallet.
        if (!notes.TryGetValue("partner_id", out var pidRaw) || !Guid.TryParse(pidRaw, out var linkPartnerId))
            throw new BusinessRuleException("This payment link has no partner binding.");
        if (linkPartnerId != partnerId)
            throw new ForbiddenException("This payment link belongs to a different partner.");

        // 3. The dedup key is the server-set notes key (fixed per link) — NOT client input. A repeat
        //    sync re-reads the same key, so AppendAsync returns the original credit without re-crediting.
        if (!notes.TryGetValue("idempotency_key", out var key) || string.IsNullOrWhiteSpace(key))
            throw new BusinessRuleException("This payment link has no idempotency key.");

        if (details.AmountPaidMajor <= 0)
            throw new BusinessRuleException("Razorpay reports the link paid but with a zero amount.");

        // Credit Razorpay's authoritative amount_paid — never a client-supplied figure.
        var txn = await PartnerWalletLedger.AppendAsync(
            _db, partnerId, direction: 1, amount: details.AmountPaidMajor,
            referenceType: "topup", referenceId: null, idempotencyKey: key,
            notes: "Razorpay payment-link top-up", currencyCode: PartnerWalletMap.DefaultCurrency,
            actorId: null, ct);

        return new SyncPartnerWalletTopUpResponse(details.Status, PartnerWalletMap.ToTxnDto(txn));
    }
}
