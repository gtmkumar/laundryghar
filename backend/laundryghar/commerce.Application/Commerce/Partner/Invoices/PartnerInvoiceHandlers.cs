using commerce.Application.Common.Interfaces;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Exceptions;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Commerce.Partner.Invoices;

// ── List my invoices (paged) ────────────────────────────────────────────────────

/// <summary>Lists the calling partner's invoices, newest billing period first. partner_id comes from
/// the tenant context; the rls_partner policy independently scopes rows to app.current_partner_id, so
/// a partner can never read another partner's invoices. Endpoint-gated to PartnerAdmin (docs/rbac.md
/// §13 — billing is admin-only).</summary>
public sealed record GetPartnerInvoicesQuery(int Page, int PageSize) : IQuery<PaginatedList<PartnerInvoiceListItemDto>>;

public sealed class GetPartnerInvoicesHandler
    : IQueryHandler<GetPartnerInvoicesQuery, PaginatedList<PartnerInvoiceListItemDto>>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentTenant _tenant;

    public GetPartnerInvoicesHandler(ICommerceDbContext db, ICurrentTenant tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public Task<PaginatedList<PartnerInvoiceListItemDto>> HandleAsync(GetPartnerInvoicesQuery q, CancellationToken ct)
    {
        var partnerId = _tenant.PartnerId
            ?? throw new UnauthorizedAccessException("Partner context required.");

        var query = _db.PartnerInvoices
            .Where(x => x.PartnerId == partnerId)
            .OrderByDescending(x => x.BillingPeriodStart)
            .Select(x => new PartnerInvoiceListItemDto(
                x.Id, x.InvoiceNumber, x.BillingPeriodStart, x.BillingPeriodEnd,
                x.GrandTotal, x.AmountPaid, x.AmountDue, x.CurrencyCode, x.Status,
                x.IssuedAt, x.DueAt, x.PaidAt));

        return PaginatedList<PartnerInvoiceListItemDto>.CreateAsync(query, q.Page, q.PageSize, ct);
    }
}

// ── Get one invoice ─────────────────────────────────────────────────────────────

/// <summary>Returns one of the calling partner's invoices, or null (→ 404) when not found / not the
/// caller's (RLS filters cross-partner rows out, so a foreign id simply returns null).</summary>
public sealed record GetPartnerInvoiceByIdQuery(Guid InvoiceId) : IQuery<PartnerInvoiceDto?>;

public sealed class GetPartnerInvoiceByIdHandler : IQueryHandler<GetPartnerInvoiceByIdQuery, PartnerInvoiceDto?>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentTenant _tenant;

    public GetPartnerInvoiceByIdHandler(ICommerceDbContext db, ICurrentTenant tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<PartnerInvoiceDto?> HandleAsync(GetPartnerInvoiceByIdQuery q, CancellationToken ct)
    {
        var partnerId = _tenant.PartnerId
            ?? throw new UnauthorizedAccessException("Partner context required.");

        var inv = await _db.PartnerInvoices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == q.InvoiceId && x.PartnerId == partnerId, ct);
        return inv is null ? null : PartnerInvoiceMap.ToDto(inv);
    }
}

// ── Get the invoice PDF url (or 404 stub — no renderer this wave) ────────────────

/// <summary>Returns the stored <c>InvoicePdfUrl</c> for the caller's invoice, or null when either
/// the invoice is not found/not the caller's OR no PDF has been rendered yet. NOTE: this wave does NOT
/// build a PDF renderer — the endpoint just surfaces a pre-stored URL; rendering is a follow-up.</summary>
public sealed record GetPartnerInvoicePdfUrlQuery(Guid InvoiceId) : IQuery<string?>;

public sealed class GetPartnerInvoicePdfUrlHandler : IQueryHandler<GetPartnerInvoicePdfUrlQuery, string?>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentTenant _tenant;

    public GetPartnerInvoicePdfUrlHandler(ICommerceDbContext db, ICurrentTenant tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<string?> HandleAsync(GetPartnerInvoicePdfUrlQuery q, CancellationToken ct)
    {
        var partnerId = _tenant.PartnerId
            ?? throw new UnauthorizedAccessException("Partner context required.");

        return await _db.PartnerInvoices.AsNoTracking()
            .Where(x => x.Id == q.InvoiceId && x.PartnerId == partnerId)
            .Select(x => x.InvoicePdfUrl)
            .FirstOrDefaultAsync(ct);
    }
}

// ── Pay an invoice: create a Razorpay payment link for AmountDue ─────────────────

/// <summary>
/// Generate (or return the existing) Razorpay Payment Link for AmountDue on an issued partner invoice.
/// Stores the link id + URL and returns the payable short URL. Idempotent: a second call returns the
/// stored link. Reconciliation (marking the invoice paid) happens out-of-band via the partner paylink
/// webhook (push) or <see cref="SyncPartnerInvoicePaymentCommand"/> (pull) — this command only creates
/// the collectible link, mirroring core's CreateBrandPlatformInvoicePaymentLinkCommand.
/// </summary>
public sealed record PayPartnerInvoiceCommand(Guid InvoiceId) : ICommand<PayPartnerInvoiceResponse>;

public sealed class PayPartnerInvoiceHandler : ICommandHandler<PayPartnerInvoiceCommand, PayPartnerInvoiceResponse>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly IPartnerPaymentLinkClient _rzp;

    public PayPartnerInvoiceHandler(ICommerceDbContext db, ICurrentTenant tenant, IPartnerPaymentLinkClient rzp)
    {
        _db = db;
        _tenant = tenant;
        _rzp = rzp;
    }

    public async Task<PayPartnerInvoiceResponse> HandleAsync(PayPartnerInvoiceCommand cmd, CancellationToken ct)
    {
        var partnerId = _tenant.PartnerId
            ?? throw new UnauthorizedAccessException("Partner context required.");

        var inv = await _db.PartnerInvoices
            .FirstOrDefaultAsync(x => x.Id == cmd.InvoiceId && x.PartnerId == partnerId, ct)
            ?? throw new KeyNotFoundException("Partner invoice not found.");

        // Idempotent: return the already-generated link.
        if (!string.IsNullOrEmpty(inv.PaymentLinkUrl))
            return new PayPartnerInvoiceResponse(inv.Id, inv.PaymentLinkUrl, inv.Status);

        if (!string.Equals(inv.Status, "issued", StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("Only an issued invoice can be collected.");

        var amountDue = inv.AmountDue ?? (inv.GrandTotal - inv.AmountPaid);
        if (amountDue <= 0)
            throw new BusinessRuleException("This invoice has nothing due.");

        if (!await _rzp.IsConfiguredAsync(ct))
            throw new BusinessRuleException(
                "Razorpay is not configured. Enable it under Settings → Platform billing, or set Razorpay:KeyId / Razorpay:KeySecret.");

        var desc = $"Partner invoice {inv.InvoiceNumber} · {inv.BillingPeriodStart:dd MMM} – {inv.BillingPeriodEnd:dd MMM yyyy}";
        var link = await _rzp.CreatePaymentLinkAsync(
            amountDue, inv.CurrencyCode, desc, referenceId: inv.Id.ToString(),
            notes: new Dictionary<string, string>
            {
                ["kind"] = "partner_invoice",
                ["partner_invoice_id"] = inv.Id.ToString(),
                ["partner_id"] = partnerId.ToString(),
            }, ct);

        inv.RazorpayPaymentLinkId = link.Id;
        inv.PaymentLinkUrl = link.ShortUrl;
        inv.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new PayPartnerInvoiceResponse(inv.Id, link.ShortUrl, inv.Status);
    }
}

// ── Sync an invoice against its Razorpay link status (pull reconcile) ────────────

/// <summary>Pull the payment link's status from Razorpay; if paid, mark the invoice paid
/// (amount_paid := grand_total → generated amount_due := 0). Returns the resulting status. This is the
/// pull-side twin of the webhook push reconcile; it uses the API key (no webhook secret needed) and
/// runs entirely in the caller's partner RLS scope. Mirrors core's SyncBrandPlatformInvoicePaymentCommand.</summary>
public sealed record SyncPartnerInvoicePaymentCommand(Guid InvoiceId) : ICommand<string?>;

public sealed class SyncPartnerInvoicePaymentHandler : ICommandHandler<SyncPartnerInvoicePaymentCommand, string?>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly IPartnerPaymentLinkClient _rzp;

    public SyncPartnerInvoicePaymentHandler(ICommerceDbContext db, ICurrentTenant tenant, IPartnerPaymentLinkClient rzp)
    {
        _db = db;
        _tenant = tenant;
        _rzp = rzp;
    }

    public async Task<string?> HandleAsync(SyncPartnerInvoicePaymentCommand cmd, CancellationToken ct)
    {
        var partnerId = _tenant.PartnerId
            ?? throw new UnauthorizedAccessException("Partner context required.");

        var inv = await _db.PartnerInvoices
            .FirstOrDefaultAsync(x => x.Id == cmd.InvoiceId && x.PartnerId == partnerId, ct)
            ?? throw new KeyNotFoundException("Partner invoice not found.");

        if (string.IsNullOrEmpty(inv.RazorpayPaymentLinkId))
            throw new BusinessRuleException("This invoice has no Razorpay payment link to sync.");
        if (string.Equals(inv.Status, "paid", StringComparison.OrdinalIgnoreCase))
            return inv.Status;

        var details = await _rzp.GetPaymentLinkAsync(inv.RazorpayPaymentLinkId, ct);
        if (string.Equals(details.Status, "paid", StringComparison.OrdinalIgnoreCase))
        {
            PartnerInvoiceMap.MarkPaid(inv);
            await _db.SaveChangesAsync(ct);
        }
        return inv.Status;
    }
}

// ── Mapper + shared invoice mutations ───────────────────────────────────────────

internal static class PartnerInvoiceMap
{
    public static PartnerInvoiceDto ToDto(PartnerInvoice x) => new(
        x.Id, x.PartnerId, x.InvoiceNumber, x.BillingPeriodStart, x.BillingPeriodEnd,
        x.LineItems, x.Subtotal, x.TaxTotal, x.GrandTotal, x.AmountPaid, x.AmountDue,
        x.CurrencyCode, x.Status, x.InvoicePdfUrl, x.PaymentLinkUrl,
        x.IssuedAt, x.DueAt, x.PaidAt, x.CreatedAt, x.UpdatedAt);

    /// <summary>Marks an invoice fully paid: amount_paid := grand_total (the DB-generated amount_due
    /// then becomes 0), status := paid, paid_at := now. Idempotent-safe to call once from either the
    /// webhook or the pull-sync path — the callers guard against a second transition.</summary>
    public static void MarkPaid(PartnerInvoice inv)
    {
        var now = DateTimeOffset.UtcNow;
        inv.AmountPaid = inv.GrandTotal;
        inv.Status = "paid";
        inv.PaidAt = now;
        inv.UpdatedAt = now;
    }
}
