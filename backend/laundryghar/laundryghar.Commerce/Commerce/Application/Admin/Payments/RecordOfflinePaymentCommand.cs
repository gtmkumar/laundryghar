using FluentValidation;
using laundryghar.Commerce.Application;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using MediatR;

using laundryghar.Commerce.Infrastructure.Services;
namespace laundryghar.Commerce.Application.Admin.Payments;

// ── Request / Response DTOs ───────────────────────────────────────────────────

public sealed record RecordOfflinePaymentRequest(
    Guid OrderId,
    /// <summary>Method: cash | upi | card (mapped to payment_mode / gateway).</summary>
    string Method,
    decimal Amount,
    /// <summary>Optional reference number (transaction ID, UPI ref, etc.).</summary>
    string? Reference,
    /// <summary>
    /// Client-supplied idempotency key (from the <c>Idempotency-Key</c> HTTP header or
    /// body field). When present it takes precedence over the server-derived key.
    /// Allows POS retries to safely re-post the same payment without creating duplicates.
    /// </summary>
    string? IdempotencyKey = null
);

public sealed record OfflinePaymentDto(
    Guid PaymentId,
    Guid OrderId,
    string Method,
    decimal Amount,
    string? Reference,
    string OrderPaymentStatus,
    decimal OrderAmountPaid,
    decimal? OrderAmountDue
);

// ── Command + Handler ─────────────────────────────────────────────────────────

public sealed record RecordOfflinePaymentCommand(
    RecordOfflinePaymentRequest Request,
    Guid? ActorId
) : IRequest<OfflinePaymentDto>;

/// <summary>
/// Records an offline counter payment (cash / UPI / card) against an order.
///
/// Contract:
///   1. Validate the order exists and belongs to this brand.
///   2. Cumulative guard: total AmountPaid + new Amount must not exceed GrandTotal (422).
///   3. Idempotency: orderId + amount + reference → deduplication key; re-plays the
///      existing payment row without creating a duplicate.
///   4. Creates commerce.payments row (status = captured, direction = 1, purpose = order).
///   5. Updates order_lifecycle.orders.amount_paid and payment_status (shared DbContext).
///   6. For cash tenders: best-effort cash-book entry using today's open full_day book for
///      the order's store, mirroring SettleRiderCodHandler — settlement succeeds even when
///      no open book exists (no-op, not an error).
///   All writes in a single execution-strategy transaction.
/// </summary>
public sealed class RecordOfflinePaymentHandler : IRequestHandler<RecordOfflinePaymentCommand, OfflinePaymentDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    private static class Cb
    {
        public const string ShiftFullDay         = "full_day";
        public const string StatusOpen           = "open";
        public const string EntryCashIn          = "cash_in";
        public const string CategoryOrderPayment = "order_payment";
        public const string PaymentModeCash      = "cash";
        public const string RefOfflinePayment    = "offline_payment";
    }

    public RecordOfflinePaymentHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db  = db;
        _user = user;
    }

    public async Task<OfflinePaymentDto> Handle(RecordOfflinePaymentCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        // ── Load order (cross-BC: Orders schema lives in the shared DbContext) ──
        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == req.OrderId && o.BrandId == brandId && o.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException($"Order {req.OrderId} not found.");

        // ── Idempotency: client-supplied key takes precedence; fall back to
        //    server-derived (orderId + amount + reference) when absent.
        //    H3b fix: honour req.IdempotencyKey so that POS retries with an
        //    explicit header are deduplicated correctly even if the amount or
        //    reference differs on the retry (e.g. due to a rounding display bug).
        var idemKey = !string.IsNullOrWhiteSpace(req.IdempotencyKey)
            ? req.IdempotencyKey.Trim()
            : BuildIdempotencyKey(req.OrderId, req.Amount, req.Reference);
        var existing = await _db.Payments
            .FirstOrDefaultAsync(p => p.IdempotencyKey == idemKey && p.BrandId == brandId, ct);
        if (existing is not null)
        {
            return new OfflinePaymentDto(
                existing.Id, req.OrderId, req.Method, existing.Amount,
                req.Reference, order.PaymentStatus, order.AmountPaid, order.AmountDue);
        }

        // ── Cumulative guard ──────────────────────────────────────────────────
        // Include already-captured payments for this order (not failed/cancelled).
        var alreadyPaid = await _db.Payments
            .Where(p => p.OrderId == req.OrderId && p.BrandId == brandId
                     && p.Status != CommercePaymentStatus.Failed
                     && p.Status != CommercePaymentStatus.Cancelled)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        if (alreadyPaid + req.Amount > order.GrandTotal)
            throw new BusinessRuleException(
                $"Payment of {req.Amount:F2} would exceed order total of {order.GrandTotal:F2}. " +
                $"Already collected: {alreadyPaid:F2}.");

        // ── Derive payment number ────────────────────────────────────────────
        var paymentNumber = $"OFFPAY-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..30];

        // ── Map method → gateway / payment_mode ─────────────────────────────
        var (gateway, payMode) = req.Method.ToLowerInvariant() switch
        {
            "upi"  => ("upi_offline",  "upi"),
            "card" => ("card_offline", "card"),
            _      => ("cash",         "cash")   // default = cash
        };

        var payment = new Payment
        {
            Id             = Guid.NewGuid(),
            BrandId        = brandId,
            FranchiseId    = order.FranchiseId,
            StoreId        = order.StoreId,
            CustomerId     = order.CustomerId,
            OrderId        = order.Id,
            OrderCreatedAt = order.CreatedAt,
            PaymentPurpose = "order",
            PaymentNumber  = paymentNumber,
            Amount         = req.Amount,
            ConvenienceFee = 0m,
            GatewayCharge  = 0m,
            NetAmount      = req.Amount,
            CurrencyCode   = order.CurrencyCode,
            Direction      = 1,
            Gateway        = gateway,
            Notes          = req.Reference,
            Status         = CommercePaymentStatus.Captured,
            IdempotencyKey = idemKey,
            InitiatedAt    = now,
            CompletedAt    = now,
            Metadata       = "{}",
            CreatedAt      = now,
            UpdatedAt      = now,
            CreatedBy      = cmd.ActorId,
            UpdatedBy      = cmd.ActorId
        };

        // ── Update order amount_paid + payment_status ─────────────────────────
        var newAmountPaid = order.AmountPaid + req.Amount;
        order.AmountPaid     = newAmountPaid;
        order.PaymentStatus  = newAmountPaid >= order.GrandTotal ? "paid" : "partial";
        order.UpdatedAt      = now;
        order.UpdatedBy      = cmd.ActorId;
        order.Version++;

        _db.Payments.Add(payment);

        // ── Cash-book entry (best-effort, cash only) ─────────────────────────
        if (payMode == "cash")
            await PostCashBookEntryAsync(payment, order, brandId, now, cmd.ActorId, ct);

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return new OfflinePaymentDto(
            payment.Id, order.Id, req.Method, payment.Amount,
            req.Reference, order.PaymentStatus, order.AmountPaid, order.AmountDue);
    }

    // Idempotency key: deterministic from (orderId, amount rounded to 2dp, reference).
    private static string BuildIdempotencyKey(Guid orderId, decimal amount, string? reference)
    {
        var refPart = string.IsNullOrWhiteSpace(reference) ? "noref" : reference.Trim().ToLowerInvariant();
        return $"offline:{orderId}:{amount:F2}:{refPart}"[..Math.Min(200, $"offline:{orderId}:{amount:F2}:{refPart}".Length)];
    }

    /// <summary>
    /// Posts a cash_in / order_payment entry into the store's open full_day cash book.
    /// Opens a fresh book when none exists for the day; silently skips when an existing
    /// book is not in status = open. Tracked on the shared context — committed by caller.
    /// </summary>
    private async Task PostCashBookEntryAsync(
        Payment payment, SharedDataModel.Entities.OrderLifecycle.Order order,
        Guid brandId, DateTimeOffset now, Guid? actorId, CancellationToken ct)
    {
        if (payment.Amount <= 0m) return;
        if (order.StoreId == Guid.Empty) return;  // guard: store required for cash-book lookup

        var bookDate = DateOnly.FromDateTime(now.UtcDateTime);

        var book = await _db.CashBooks.FirstOrDefaultAsync(
            b => b.BrandId == brandId && b.StoreId == order.StoreId
              && b.BookDate == bookDate && b.ShiftLabel == Cb.ShiftFullDay, ct);

        if (book is null)
        {
            book = new CashBook
            {
                Id             = Guid.NewGuid(),
                BrandId        = brandId,
                FranchiseId    = order.FranchiseId,
                StoreId        = order.StoreId,
                BookDate       = bookDate,
                ShiftLabel     = Cb.ShiftFullDay,
                OpeningUserId  = actorId ?? Guid.Empty,
                OpeningBalance = 0m,
                CashInflow     = 0m,
                CashOutflow    = 0m,
                UpiInflow      = 0m,
                CardInflow     = 0m,
                OtherInflow    = 0m,
                DepositAmount  = 0m,
                TotalOrders    = 0,
                NewOrders      = 0,
                DeliveredOrders  = 0,
                CancelledOrders  = 0,
                Status         = Cb.StatusOpen,
                Metadata       = "{}",
                OpenedAt       = now,
                CreatedAt      = now,
                UpdatedAt      = now,
                CreatedBy      = actorId,
                UpdatedBy      = actorId
            };
            _db.CashBooks.Add(book);
        }
        else if (book.Status != Cb.StatusOpen)
        {
            return;   // closed book → skip, payment still recorded
        }

        _db.CashBookEntries.Add(new CashBookEntry
        {
            Id            = Guid.NewGuid(),
            CashBookId    = book.Id,
            BrandId       = brandId,
            StoreId       = order.StoreId,
            EntryType     = Cb.EntryCashIn,
            Category      = Cb.CategoryOrderPayment,
            Direction     = 1,
            Amount        = payment.Amount,
            PaymentMode   = Cb.PaymentModeCash,
            ReferenceType = Cb.RefOfflinePayment,
            ReferenceId   = payment.Id,
            OrderId       = order.Id,
            OrderCreatedAt = order.CreatedAt,
            CustomerId    = order.CustomerId,
            Description   = $"Counter payment — Order {order.OrderNumber}",
            PerformedBy   = actorId ?? Guid.Empty,
            OccurredAt    = now,
            Metadata      = "{}",
            CreatedAt     = now,
            CreatedBy     = actorId
        });

        book.CashInflow += payment.Amount;
        book.UpdatedAt  = now;
        book.UpdatedBy  = actorId;
    }
}

// ── Validator ─────────────────────────────────────────────────────────────────

public sealed class RecordOfflinePaymentValidator : AbstractValidator<RecordOfflinePaymentCommand>
{
    private static readonly string[] AllowedMethods = ["cash", "upi", "card"];

    public RecordOfflinePaymentValidator()
    {
        RuleFor(x => x.Request.OrderId).NotEmpty();
        RuleFor(x => x.Request.Amount).GreaterThan(0).WithMessage("Amount must be greater than zero.");
        RuleFor(x => x.Request.Method)
            .NotEmpty()
            .Must(m => AllowedMethods.Contains(m?.ToLowerInvariant()))
            .WithMessage($"Method must be one of: {string.Join(", ", AllowedMethods)}.");
        RuleFor(x => x.Request.Reference)
            .MaximumLength(200).When(x => x.Request.Reference is not null);
    }
}
