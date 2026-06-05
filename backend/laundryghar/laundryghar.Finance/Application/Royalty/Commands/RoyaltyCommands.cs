using FluentValidation;
using laundryghar.Finance.Application.Royalty.Dtos;
using MediatR;

namespace laundryghar.Finance.Application.Royalty.Commands;

// ── Shared mapping ────────────────────────────────────────────────────────────

internal static class RoyaltyMapper
{
    internal static RoyaltyInvoiceDto ToDto(RoyaltyInvoice inv) => new(
        inv.Id, inv.BrandId, inv.FranchiseId, inv.FranchiseAgreementId,
        inv.InvoiceNumber, inv.PeriodStart, inv.PeriodEnd,
        inv.GrossRevenue, inv.EligibleRevenue,
        inv.RoyaltyPercent, inv.RoyaltyAmount,
        inv.MarketingFeePercent, inv.MarketingFeeAmount,
        inv.TechnologyFeeAmount, inv.OtherCharges, inv.Adjustments,
        inv.Subtotal, inv.TaxTotal, inv.GrandTotal,
        inv.AmountPaid, inv.AmountDue,
        inv.CurrencyCode, inv.TotalOrders,
        inv.InvoiceDate, inv.DueDate,
        inv.Status, inv.Notes, inv.CreatedAt,
        inv.Calculations.Select(ToCalcDto).ToList());

    internal static RoyaltyCalculationDto ToCalcDto(RoyaltyCalculation c) => new(
        c.Id, c.RoyaltyInvoiceId, c.StoreId, c.OrderId,
        c.CalculationDate, c.RevenueType,
        c.GrossAmount, c.ExcludedAmount, c.EligibleAmount,
        c.RoyaltyRate, c.RoyaltyAmount, c.Notes);
}

// ── Generate / Calculate Royalty Invoice (draft) ──────────────────────────────
// Decision: if GrossRevenueOverride is provided, use it directly.
// If not, sum completed payments in commerce.payments for the franchise period.
// Royalty lines: one "adjustment" type line per calculation summarising the period.
// This keeps the implementation simple & documented; per-order lines can be added later.

public sealed record GenerateRoyaltyInvoiceCommand(GenerateRoyaltyInvoiceRequest Request, Guid? ActorId)
    : IRequest<RoyaltyInvoiceDto>;

public sealed class GenerateRoyaltyInvoiceHandler
    : IRequestHandler<GenerateRoyaltyInvoiceCommand, RoyaltyInvoiceDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;

    public GenerateRoyaltyInvoiceHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RoyaltyInvoiceDto> Handle(GenerateRoyaltyInvoiceCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        // Verify franchise belongs to brand
        var franchise = await _db.Franchises
            .FirstOrDefaultAsync(f => f.Id == req.FranchiseId && f.BrandId == brandId, ct);
        if (franchise is null)
            throw new BusinessRuleException("Franchise not found.");

        // Prevent duplicate invoice for same franchise + period
        var exists = await _db.RoyaltyInvoices.AnyAsync(
            i => i.FranchiseId  == req.FranchiseId
              && i.BrandId      == brandId
              && i.PeriodStart  == req.PeriodStart
              && i.PeriodEnd    == req.PeriodEnd, ct);
        if (exists)
            throw new BusinessRuleException(
                $"A royalty invoice already exists for this franchise and period ({req.PeriodStart}–{req.PeriodEnd}).");

        // ── Revenue resolution ───────────────────────────────────────────────
        // Decision: use override if provided; otherwise aggregate payments.
        // commerce.payments stores franchise_id on orders; we aggregate completed payments.
        decimal grossRevenue;
        int     totalOrders = 0;

        if (req.GrossRevenueOverride.HasValue)
        {
            grossRevenue = req.GrossRevenueOverride.Value;
        }
        else
        {
            // Period filter as DateTimeOffset range (inclusive)
            var periodFrom = new DateTimeOffset(req.PeriodStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var periodTo   = new DateTimeOffset(req.PeriodEnd.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

            // Sum completed payments linked to this franchise's orders
            var aggResult = await _db.Payments
                .Where(p => p.BrandId    == brandId
                         && p.FranchiseId == req.FranchiseId
                         && p.Status      == "completed"
                         && p.CompletedAt >= periodFrom
                         && p.CompletedAt <= periodTo)
                .GroupBy(_ => 1)
                .Select(g => new { Total = g.Sum(p => p.Amount), Count = g.Count() })
                .FirstOrDefaultAsync(ct);

            grossRevenue = aggResult?.Total ?? 0;
            totalOrders  = aggResult?.Count ?? 0;
        }

        // ── Calculation ───────────────────────────────────────────────────────
        var eligibleRevenue    = grossRevenue; // no exclusions in simple model
        var royaltyAmount      = Math.Round(eligibleRevenue * req.RoyaltyPercent / 100, 2);
        var marketingFeeAmount = Math.Round(eligibleRevenue * req.MarketingFeePercent / 100, 2);
        var subtotal = royaltyAmount + marketingFeeAmount
                     + req.TechnologyFeeAmount + req.OtherCharges + req.Adjustments;
        // GST split: IGST for inter-state (simplified — use full rate as IGST)
        var taxTotal  = Math.Round(subtotal * req.GstRate / 100, 2);
        var grandTotal = subtotal + taxTotal;

        // ── Invoice number ────────────────────────────────────────────────────
        var invCount  = await _db.RoyaltyInvoices.CountAsync(i => i.BrandId == brandId, ct);
        var invNumber = $"ROY-{now:yyyyMMdd}-{(invCount + 1):D4}";

        var invoice = new RoyaltyInvoice
        {
            Id                  = Guid.NewGuid(),
            BrandId             = brandId,
            FranchiseId         = req.FranchiseId,
            FranchiseAgreementId = req.FranchiseAgreementId,
            InvoiceNumber       = invNumber,
            PeriodStart         = req.PeriodStart,
            PeriodEnd           = req.PeriodEnd,
            GrossRevenue        = grossRevenue,
            EligibleRevenue     = eligibleRevenue,
            RoyaltyPercent      = req.RoyaltyPercent,
            RoyaltyAmount       = royaltyAmount,
            MarketingFeePercent = req.MarketingFeePercent,
            MarketingFeeAmount  = marketingFeeAmount,
            TechnologyFeeAmount = req.TechnologyFeeAmount,
            OtherCharges        = req.OtherCharges,
            Adjustments         = req.Adjustments,
            Subtotal            = subtotal,
            Cgst                = 0,
            Sgst                = 0,
            Igst                = taxTotal,      // simplified: full GST as IGST
            TaxTotal            = taxTotal,
            GrandTotal          = grandTotal,
            AmountPaid          = 0,
            // AmountDue is generated — do NOT set
            CurrencyCode        = req.CurrencyCode,
            TotalOrders         = totalOrders,
            InvoiceDate         = DateOnly.FromDateTime(now.UtcDateTime),
            DueDate             = DateOnly.FromDateTime(now.UtcDateTime.AddDays(30)),
            LineItems           = "[]",
            Notes               = req.Notes,
            Status              = "draft",
            Metadata            = "{}",
            CreatedAt           = now,
            UpdatedAt           = now,
            CreatedBy           = cmd.ActorId,
            UpdatedBy           = cmd.ActorId
        };

        _db.RoyaltyInvoices.Add(invoice);
        await _db.SaveChangesAsync(ct);

        // ── Royalty calculation line(s) ───────────────────────────────────────
        // One summary "adjustment" line covering the full period gross revenue.
        // Revenue type "adjustment" is valid per CHECK constraint.
        var calcLine = new RoyaltyCalculation
        {
            Id               = Guid.NewGuid(),
            RoyaltyInvoiceId = invoice.Id,
            BrandId          = brandId,
            FranchiseId      = req.FranchiseId,
            CalculationDate  = DateOnly.FromDateTime(now.UtcDateTime),
            RevenueType      = "adjustment",    // valid CHECK value; summary line
            GrossAmount      = grossRevenue,
            ExcludedAmount   = 0,
            EligibleAmount   = eligibleRevenue,
            RoyaltyRate      = req.RoyaltyPercent,
            RoyaltyAmount    = royaltyAmount,
            Notes            = $"Period: {req.PeriodStart}–{req.PeriodEnd}; gross revenue summary.",
            CreatedAt        = now,
            CreatedBy        = cmd.ActorId
        };

        _db.RoyaltyCalculations.Add(calcLine);
        await _db.SaveChangesAsync(ct);

        // Reload to pick up generated amount_due
        await _db.Entry(invoice).ReloadAsync(ct);
        invoice.Calculations.Add(calcLine);

        return RoyaltyMapper.ToDto(invoice);
    }
}

public sealed class GenerateRoyaltyInvoiceValidator : AbstractValidator<GenerateRoyaltyInvoiceCommand>
{
    public GenerateRoyaltyInvoiceValidator()
    {
        RuleFor(x => x.Request.FranchiseId).NotEmpty();
        RuleFor(x => x.Request.PeriodStart).NotEqual(default(DateOnly));
        RuleFor(x => x.Request.PeriodEnd).NotEqual(default(DateOnly));
        RuleFor(x => x.Request)
            .Must(r => r.PeriodEnd >= r.PeriodStart)
            .WithMessage("PeriodEnd must be on or after PeriodStart.");
        RuleFor(x => x.Request.RoyaltyPercent).InclusiveBetween(0, 100);
        RuleFor(x => x.Request.MarketingFeePercent).InclusiveBetween(0, 100);
        RuleFor(x => x.Request.TechnologyFeeAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.OtherCharges).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.GstRate).InclusiveBetween(0, 100);
        RuleFor(x => x.Request.CurrencyCode).NotEmpty().MaximumLength(3);
    }
}

// ── Issue Invoice (draft → issued) ────────────────────────────────────────────

public sealed record IssueRoyaltyInvoiceCommand(Guid Id, IssueRoyaltyInvoiceRequest Request, Guid? ActorId)
    : IRequest<RoyaltyInvoiceDto?>;

public sealed class IssueRoyaltyInvoiceHandler
    : IRequestHandler<IssueRoyaltyInvoiceCommand, RoyaltyInvoiceDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    public IssueRoyaltyInvoiceHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RoyaltyInvoiceDto?> Handle(IssueRoyaltyInvoiceCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var invoice = await _db.RoyaltyInvoices
            .Include(i => i.Calculations)
            .FirstOrDefaultAsync(i => i.Id == cmd.Id && i.BrandId == brandId, ct);
        if (invoice is null) return null;

        if (invoice.Status != "draft")
            throw new BusinessRuleException($"Only draft invoices can be issued. Current status: '{invoice.Status}'.");

        var now = DateTimeOffset.UtcNow;
        invoice.Status    = "issued";
        invoice.SentAt    = now;
        invoice.Notes     = cmd.Request.Notes ?? invoice.Notes;
        invoice.UpdatedAt = now;
        invoice.UpdatedBy = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        await _db.Entry(invoice).ReloadAsync(ct);

        return RoyaltyMapper.ToDto(invoice);
    }
}

// ── Record Payment ────────────────────────────────────────────────────────────

public sealed record RecordRoyaltyPaymentCommand(Guid Id, RecordRoyaltyPaymentRequest Request, Guid? ActorId)
    : IRequest<RoyaltyInvoiceDto?>;

public sealed class RecordRoyaltyPaymentHandler
    : IRequestHandler<RecordRoyaltyPaymentCommand, RoyaltyInvoiceDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    public RecordRoyaltyPaymentHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RoyaltyInvoiceDto?> Handle(RecordRoyaltyPaymentCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var invoice = await _db.RoyaltyInvoices
            .Include(i => i.Calculations)
            .FirstOrDefaultAsync(i => i.Id == cmd.Id && i.BrandId == brandId, ct);
        if (invoice is null) return null;

        if (invoice.Status is not ("issued" or "sent" or "viewed" or "partial" or "overdue"))
            throw new BusinessRuleException(
                $"Cannot record payment for invoice with status '{invoice.Status}'.");

        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        invoice.AmountPaid += req.AmountPaid;
        invoice.Notes       = req.Notes ?? invoice.Notes;
        invoice.UpdatedAt   = now;
        invoice.UpdatedBy   = cmd.ActorId;

        // Status update based on remaining balance (AmountDue is DB-generated;
        // compute locally for status decision before reload)
        var remaining = invoice.GrandTotal - invoice.AmountPaid;
        if (remaining <= 0)
        {
            invoice.Status = "paid";
            invoice.PaidAt = now;
        }
        else
        {
            invoice.Status = "partial";
        }

        await _db.SaveChangesAsync(ct);
        await _db.Entry(invoice).ReloadAsync(ct);

        return RoyaltyMapper.ToDto(invoice);
    }
}

public sealed class RecordRoyaltyPaymentValidator : AbstractValidator<RecordRoyaltyPaymentCommand>
{
    public RecordRoyaltyPaymentValidator()
    {
        RuleFor(x => x.Request.AmountPaid).GreaterThan(0);
    }
}
