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

/// <summary>Pull the top-up link's status from Razorpay; if paid, credit the wallet ONCE via the
/// shared ledger primitive (idempotency_key = the top-up key). The credited amount is the authoritative
/// amount_paid reported by Razorpay, never a client-supplied figure. Runs in the caller's partner RLS
/// scope. The webhook does the same push-side with the same key, so the two paths converge.</summary>
public sealed record SyncPartnerWalletTopUpCommand(string LinkId, string IdempotencyKey)
    : ICommand<SyncPartnerWalletTopUpResponse>;

public sealed class SyncPartnerWalletTopUpHandler
    : ICommandHandler<SyncPartnerWalletTopUpCommand, SyncPartnerWalletTopUpResponse>
{
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
        if (string.IsNullOrWhiteSpace(cmd.IdempotencyKey))
            throw new BusinessRuleException("An idempotency key is required.");

        var details = await _rzp.GetPaymentLinkAsync(cmd.LinkId, ct);
        if (!string.Equals(details.Status, "paid", StringComparison.OrdinalIgnoreCase))
            return new SyncPartnerWalletTopUpResponse(details.Status, Transaction: null);

        if (details.AmountPaidMajor <= 0)
            throw new BusinessRuleException("Razorpay reports the link paid but with a zero amount.");

        var txn = await PartnerWalletLedger.AppendAsync(
            _db, partnerId, direction: 1, amount: details.AmountPaidMajor,
            referenceType: "topup", referenceId: null, idempotencyKey: cmd.IdempotencyKey,
            notes: "Razorpay payment-link top-up", currencyCode: PartnerWalletMap.DefaultCurrency,
            actorId: null, ct);

        return new SyncPartnerWalletTopUpResponse(details.Status, PartnerWalletMap.ToTxnDto(txn));
    }
}
