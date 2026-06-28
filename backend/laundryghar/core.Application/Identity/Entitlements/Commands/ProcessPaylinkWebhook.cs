using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace core.Application.Identity.Entitlements.Commands;

/// <summary>Result of processing a Razorpay payment-link webhook.</summary>
public sealed record PaylinkWebhookResult(bool Accepted, string? Reason);

/// <summary>Process a Razorpay <c>payment_link.paid</c> webhook and mark the matching brand-platform
/// invoice paid. Verifies the signature (Razorpay:WebhookSecret); dev-friendly fail-open only when no
/// secret + no signature (Development).</summary>
public sealed record ProcessPaylinkWebhookCommand(byte[] RawBody, string? Signature) : ICommand<PaylinkWebhookResult>;

public class ProcessPaylinkWebhookCommandHandler : ICommandHandler<ProcessPaylinkWebhookCommand, PaylinkWebhookResult>
{
    private readonly ICoreDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly ILogger<ProcessPaylinkWebhookCommandHandler> _log;

    public ProcessPaylinkWebhookCommandHandler(ICoreDbContext db, IConfiguration config, IHostEnvironment env,
        ILogger<ProcessPaylinkWebhookCommandHandler> log)
    { _db = db; _config = config; _env = env; _log = log; }

    public async Task<PaylinkWebhookResult> HandleAsync(ProcessPaylinkWebhookCommand cmd, CancellationToken ct)
    {
        // ── Verify signature (fail-closed, with a Development-only unsigned escape hatch) ──
        var secret = _config["Razorpay:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            if (!_env.IsDevelopment()) return new PaylinkWebhookResult(false, "Razorpay:WebhookSecret not configured.");
            if (!string.IsNullOrEmpty(cmd.Signature)) return new PaylinkWebhookResult(false, "Unverifiable signature.");
            _log.LogWarning("[DEV] paylink webhook accepted UNSIGNED (Development only).");
        }
        else if (!VerifyHmac(cmd.RawBody, cmd.Signature, secret))
        {
            return new PaylinkWebhookResult(false, "Invalid signature.");
        }

        // ── Parse + act on payment_link.paid ──
        using var doc = JsonDocument.Parse(cmd.RawBody);
        var root = doc.RootElement;
        var evt = root.TryGetProperty("event", out var e) ? e.GetString() : null;
        if (evt != "payment_link.paid") return new PaylinkWebhookResult(true, $"ignored event '{evt}'");

        if (!root.TryGetProperty("payload", out var payload)
            || !payload.TryGetProperty("payment_link", out var pl)
            || !pl.TryGetProperty("entity", out var ent))
            return new PaylinkWebhookResult(true, "no payment_link entity");

        var linkId = ent.TryGetProperty("id", out var idp) ? idp.GetString() : null;
        if (string.IsNullOrEmpty(linkId)) return new PaylinkWebhookResult(true, "no link id");

        var inv = await _db.BrandPlatformInvoices.FirstOrDefaultAsync(i => i.RazorpayPaymentLinkId == linkId, ct);
        if (inv is null) return new PaylinkWebhookResult(true, "no matching invoice");
        if (!string.Equals(inv.Status, "issued", StringComparison.OrdinalIgnoreCase))
            return new PaylinkWebhookResult(true, $"invoice already {inv.Status}");

        inv.Status = "paid";
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Razorpay payment_link.paid → brand-platform invoice {InvId} marked paid (link {Link}).", inv.Id, linkId);
        return new PaylinkWebhookResult(true, "marked paid");
    }

    private static bool VerifyHmac(byte[] rawBody, string? signature, string secret)
    {
        if (string.IsNullOrEmpty(signature)) return false;
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), rawBody)).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature));
    }
}
