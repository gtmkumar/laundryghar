using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Entitlements.Commands;

// ── Create a Razorpay payment link for a brand-platform invoice ──────────────
/// <summary>Generate (or return the existing) Razorpay Payment Link for an issued brand-platform
/// invoice. Returns the payable short URL. Idempotent: a second call returns the stored link.</summary>
public sealed record CreateBrandPlatformInvoicePaymentLinkCommand(Guid InvoiceId) : ICommand<string?>;

public class CreateBrandPlatformInvoicePaymentLinkCommandHandler
    : ICommandHandler<CreateBrandPlatformInvoicePaymentLinkCommand, string?>
{
    private readonly ICoreDbContext _db;
    private readonly IRazorpayLinkClient _rzp;
    public CreateBrandPlatformInvoicePaymentLinkCommandHandler(ICoreDbContext db, IRazorpayLinkClient rzp)
    { _db = db; _rzp = rzp; }

    public async Task<string?> HandleAsync(CreateBrandPlatformInvoicePaymentLinkCommand cmd, CancellationToken ct)
    {
        var inv = await _db.BrandPlatformInvoices.FirstOrDefaultAsync(i => i.Id == cmd.InvoiceId, ct);
        if (inv is null) return null;
        if (!string.IsNullOrEmpty(inv.PaymentLinkUrl)) return inv.PaymentLinkUrl; // already has a link
        if (!string.Equals(inv.Status, "issued", StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("Only an issued invoice can be collected.");
        if (!_rzp.IsConfigured)
            throw new BusinessRuleException("Razorpay is not configured (Razorpay:KeyId / Razorpay:KeySecret).");

        var sub = await _db.BrandPlatformSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == inv.SubscriptionId, ct);
        var desc = $"Platform tier {(sub?.PlanName ?? "subscription")} · {inv.BillingPeriodStart:dd MMM} – {inv.BillingPeriodEnd:dd MMM yyyy}";

        var link = await _rzp.CreatePaymentLinkAsync(
            inv.Amount, inv.CurrencyCode, desc, referenceId: inv.Id.ToString(),
            notes: new Dictionary<string, string> { ["brand_platform_invoice_id"] = inv.Id.ToString() }, ct);

        inv.RazorpayPaymentLinkId = link.Id;
        inv.PaymentLinkUrl = link.ShortUrl;
        await _db.SaveChangesAsync(ct);
        return link.ShortUrl;
    }
}

// ── Reconcile a brand-platform invoice against its Razorpay link status ───────
/// <summary>Pull the payment link's status from Razorpay; if it has been paid, mark the invoice paid.
/// Returns the resulting invoice status. (Pull-based reconciliation; a webhook can do the same push-side.)</summary>
public sealed record SyncBrandPlatformInvoicePaymentCommand(Guid InvoiceId) : ICommand<string?>;

public class SyncBrandPlatformInvoicePaymentCommandHandler
    : ICommandHandler<SyncBrandPlatformInvoicePaymentCommand, string?>
{
    private readonly ICoreDbContext _db;
    private readonly IRazorpayLinkClient _rzp;
    public SyncBrandPlatformInvoicePaymentCommandHandler(ICoreDbContext db, IRazorpayLinkClient rzp)
    { _db = db; _rzp = rzp; }

    public async Task<string?> HandleAsync(SyncBrandPlatformInvoicePaymentCommand cmd, CancellationToken ct)
    {
        var inv = await _db.BrandPlatformInvoices.FirstOrDefaultAsync(i => i.Id == cmd.InvoiceId, ct);
        if (inv is null) return null;
        if (string.IsNullOrEmpty(inv.RazorpayPaymentLinkId))
            throw new BusinessRuleException("This invoice has no Razorpay payment link to sync.");
        if (string.Equals(inv.Status, "paid", StringComparison.OrdinalIgnoreCase)) return inv.Status;

        var status = await _rzp.GetPaymentLinkStatusAsync(inv.RazorpayPaymentLinkId, ct);
        if (string.Equals(status, "paid", StringComparison.OrdinalIgnoreCase))
        {
            inv.Status = "paid";
            await _db.SaveChangesAsync(ct);
        }
        return inv.Status;
    }
}
