using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using commerce.Application.Commerce.Partner.Invoices;
using commerce.Application.Commerce.Partner.Wallet;
using commerce.Application.Common.Interfaces;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Crypto;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace commerce.Application.Commerce.Webhooks;

/// <summary>Result of processing a partner Razorpay payment-link webhook.</summary>
public sealed record PartnerPaylinkWebhookResult(bool Accepted, string? Reason);

/// <summary>
/// Process a Razorpay <c>payment_link.paid</c> webhook for the RaaS partner-billing lane and reconcile
/// the matching partner row: mark a partner invoice paid, or credit a partner wallet top-up (once).
///
/// Distinct from commerce's customer-payment webhook (payment.captured/failed, per-brand secret): this
/// is the partner/platform lane, so the HMAC secret is the PLATFORM gateway's webhook secret
/// (payment/platform_gateway, brand_id IS NULL) — the same account that issues the links — else env
/// <c>Razorpay:WebhookSecret</c>. Mirrors core's ProcessPaylinkWebhookCommand, but reconciles COMMERCE
/// partner rows (invoices + wallet ledger) which core's ICoreDbContext cannot reach.
///
/// RLS: the endpoint sets <c>Items["bypass_rls"]=true</c> (anonymous, no partner claim) so the handler
/// can resolve the invoice by link id and credit any partner's wallet.
/// </summary>
public sealed record ProcessPartnerPaylinkWebhookCommand(byte[] RawBody, string? Signature)
    : ICommand<PartnerPaylinkWebhookResult>;

public sealed class ProcessPartnerPaylinkWebhookHandler
    : ICommandHandler<ProcessPartnerPaylinkWebhookCommand, PartnerPaylinkWebhookResult>
{
    private readonly ICommerceDbContext _db;
    private readonly IFieldCipher _cipher;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly ILogger<ProcessPartnerPaylinkWebhookHandler> _log;

    public ProcessPartnerPaylinkWebhookHandler(
        ICommerceDbContext db, IFieldCipher cipher, IConfiguration config,
        IHostEnvironment env, ILogger<ProcessPartnerPaylinkWebhookHandler> log)
    {
        _db = db; _cipher = cipher; _config = config; _env = env; _log = log;
    }

    public async Task<PartnerPaylinkWebhookResult> HandleAsync(
        ProcessPartnerPaylinkWebhookCommand cmd, CancellationToken ct)
    {
        // ── Verify signature against the PLATFORM webhook secret (fail-closed, dev unsigned escape) ──
        var platform = await LoadPlatformGatewayAsync(ct);
        var secret = !string.IsNullOrWhiteSpace(platform.WebhookSecret)
            ? platform.WebhookSecret
            : _config["Razorpay:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            if (!_env.IsDevelopment())
                return new PartnerPaylinkWebhookResult(false, "Razorpay:WebhookSecret not configured.");
            if (!string.IsNullOrEmpty(cmd.Signature))
                return new PartnerPaylinkWebhookResult(false, "Unverifiable signature.");
            _log.LogWarning("[DEV] partner paylink webhook accepted UNSIGNED (Development only).");
        }
        else if (!VerifyHmac(cmd.RawBody, cmd.Signature, secret))
        {
            return new PartnerPaylinkWebhookResult(false, "Invalid signature.");
        }

        // ── Parse + act on payment_link.paid ──
        using var doc = JsonDocument.Parse(cmd.RawBody);
        var root = doc.RootElement;
        var evt = root.TryGetProperty("event", out var e) ? e.GetString() : null;
        if (evt != "payment_link.paid") return new PartnerPaylinkWebhookResult(true, $"ignored event '{evt}'");

        if (!root.TryGetProperty("payload", out var payload)
            || !payload.TryGetProperty("payment_link", out var pl)
            || !pl.TryGetProperty("entity", out var ent))
            return new PartnerPaylinkWebhookResult(true, "no payment_link entity");

        var linkId = ent.TryGetProperty("id", out var idp) ? idp.GetString() : null;
        if (string.IsNullOrEmpty(linkId)) return new PartnerPaylinkWebhookResult(true, "no link id");

        // 1. Partner invoice? Resolve by the stored link id (no notes needed).
        var inv = await _db.PartnerInvoices.FirstOrDefaultAsync(i => i.RazorpayPaymentLinkId == linkId, ct);
        if (inv is not null)
        {
            if (string.Equals(inv.Status, "paid", StringComparison.OrdinalIgnoreCase))
                return new PartnerPaylinkWebhookResult(true, "invoice already paid");
            PartnerInvoiceMap.MarkPaid(inv);
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("payment_link.paid → partner invoice {InvId} marked paid (link {Link}).", inv.Id, linkId);
            return new PartnerPaylinkWebhookResult(true, "invoice marked paid");
        }

        // 2. Wallet top-up? The link's notes (authored when we created it) carry the kind + partner + key.
        var notes = ReadNotes(ent);
        if (notes.TryGetValue("kind", out var kind) && kind == "partner_wallet_topup")
        {
            if (!notes.TryGetValue("partner_id", out var pidRaw) || !Guid.TryParse(pidRaw, out var partnerId))
                return new PartnerPaylinkWebhookResult(true, "wallet top-up notes missing partner_id");
            if (!notes.TryGetValue("idempotency_key", out var key) || string.IsNullOrWhiteSpace(key))
                return new PartnerPaylinkWebhookResult(true, "wallet top-up notes missing idempotency_key");

            var amount = ReadAmountPaidMajor(ent);
            if (amount <= 0) return new PartnerPaylinkWebhookResult(true, "wallet top-up amount is zero");

            await PartnerWalletLedger.AppendAsync(
                _db, partnerId, direction: 1, amount: amount,
                referenceType: "topup", referenceId: null, idempotencyKey: key,
                notes: "Razorpay payment-link top-up (webhook)", currencyCode: PartnerWalletMap.DefaultCurrency,
                actorId: null, ct);
            _log.LogInformation("payment_link.paid → partner {PartnerId} wallet credited {Amount} (link {Link}).",
                partnerId, amount, linkId);
            return new PartnerPaylinkWebhookResult(true, "wallet credited");
        }

        return new PartnerPaylinkWebhookResult(true, "no matching partner row");
    }

    private async Task<PaymentGatewaySettings> LoadPlatformGatewayAsync(CancellationToken ct)
    {
        var row = await _db.SystemSettings.AsNoTracking()
            .Where(s => s.Category == "payment" && s.SettingKey == "platform_gateway"
                     && s.Status == "active" && s.BrandId == null)
            .FirstOrDefaultAsync(ct);
        return PaymentGatewaySettings.FromJson(row?.SettingValue, _cipher);
    }

    private static Dictionary<string, string> ReadNotes(JsonElement entity)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (entity.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Object)
            foreach (var p in notes.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String)
                    result[p.Name] = p.Value.GetString()!;
        return result;
    }

    private static decimal ReadAmountPaidMajor(JsonElement entity)
    {
        // Razorpay reports paise; prefer amount_paid, fall back to amount.
        long paise = 0;
        if (entity.TryGetProperty("amount_paid", out var ap) && ap.TryGetInt64(out var v)) paise = v;
        else if (entity.TryGetProperty("amount", out var a) && a.TryGetInt64(out var v2)) paise = v2;
        return paise / 100m;
    }

    private static bool VerifyHmac(byte[] rawBody, string? signature, string secret)
    {
        if (string.IsNullOrEmpty(signature)) return false;
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), rawBody)).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature));
    }
}
