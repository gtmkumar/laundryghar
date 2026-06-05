using laundryghar.Commerce.Application;
using laundryghar.Commerce.Infrastructure.Gateway;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Commerce.Application.Admin.Payments;

// ── Queries ───────────────────────────────────────────────────────────────────

public sealed record GetAdminPaymentsQuery(int Page, int PageSize, Guid? CustomerId = null) : IRequest<PaginatedList<PaymentDto>>;

public sealed class GetAdminPaymentsHandler : IRequestHandler<GetAdminPaymentsQuery, PaginatedList<PaymentDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetAdminPaymentsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<PaymentDto>> Handle(GetAdminPaymentsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query = _db.Payments
            .Where(x => x.BrandId == brandId)
            .Where(x => q.CustomerId == null || x.CustomerId == q.CustomerId)
            .OrderByDescending(x => x.InitiatedAt)
            .Select(x => ToDto(x));
        return PaginatedList<PaymentDto>.CreateAsync(query, q.Page, q.PageSize, ct);
    }

    internal static PaymentDto ToDto(Payment x) => new(
        x.Id, x.BrandId, x.CustomerId, x.PaymentPurpose, x.PaymentNumber,
        x.Amount, x.ConvenienceFee, x.GatewayCharge, x.NetAmount, x.CurrencyCode,
        x.Direction, x.Gateway, x.GatewayOrderId, x.GatewayPaymentId,
        x.Status, x.FailureCode, x.FailureMessage, x.InitiatedAt, x.CompletedAt,
        x.FailedAt, x.IdempotencyKey, x.CreatedAt, x.UpdatedAt);
}

public sealed record GetAdminPaymentByIdQuery(Guid Id) : IRequest<PaymentDto?>;

public sealed class GetAdminPaymentByIdHandler : IRequestHandler<GetAdminPaymentByIdQuery, PaymentDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetAdminPaymentByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PaymentDto?> Handle(GetAdminPaymentByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Payments.FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : GetAdminPaymentsHandler.ToDto(e);
    }
}

// ── Issue Refund ──────────────────────────────────────────────────────────────

public sealed record IssueRefundCommand(IssueRefundRequest Request, Guid? ActorId) : IRequest<PaymentRefundDto>;

public sealed class IssueRefundHandler : IRequestHandler<IssueRefundCommand, PaymentRefundDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPaymentGateway _gateway;

    public IssueRefundHandler(LaundryGharDbContext db, ICurrentUser user, IPaymentGateway gateway)
    {
        _db = db;
        _user = user;
        _gateway = gateway;
    }

    public async Task<PaymentRefundDto> Handle(IssueRefundCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;

        var payment = await _db.Payments
            .FirstOrDefaultAsync(x => x.Id == req.OriginalPaymentId && x.BrandId == brandId, ct);

        if (payment is null)
            throw new BusinessRuleException("Payment not found.");
        if (payment.Status != "captured" && payment.Status != "completed")
            throw new BusinessRuleException($"Cannot refund a payment with status '{payment.Status}'.");
        if (req.Amount <= 0 || req.Amount > payment.Amount)
            throw new BusinessRuleException("Refund amount must be > 0 and ≤ original payment amount.");

        var now = DateTimeOffset.UtcNow;
        var refundNumber = $"REF-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..30];

        string? gatewayRefundId = null;

        // One transaction: refund row + (optional wallet credit or gateway refund)
        await using var txn = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var refund = new PaymentRefund
            {
                Id                 = Guid.NewGuid(),
                BrandId            = brandId,
                OriginalPaymentId  = payment.Id,
                CustomerId         = payment.CustomerId,
                RefundNumber       = refundNumber,
                RefundType         = req.RefundType,
                Amount             = req.Amount,
                Reason             = req.Reason,
                ReasonText         = req.ReasonText,
                Notes              = req.Notes,
                Status             = "processing",
                RequestedBy        = cmd.ActorId,
                RequestedAt        = now,
                Metadata           = "{}",
                CreatedAt          = now,
                UpdatedAt          = now,
                CreatedBy          = cmd.ActorId
            };

            if (req.RefundType == "wallet" && payment.CustomerId.HasValue)
            {
                // Credit the wallet in the same transaction
                var wallet = await _db.WalletAccounts
                    .FirstOrDefaultAsync(w => w.CustomerId == payment.CustomerId.Value && w.BrandId == brandId, ct);

                if (wallet is null)
                {
                    // Create wallet on first encounter
                    wallet = new WalletAccount
                    {
                        Id             = Guid.NewGuid(),
                        BrandId        = brandId,
                        CustomerId     = payment.CustomerId.Value,
                        CurrencyCode   = payment.CurrencyCode,
                        Balance        = 0m,
                        LockedBalance  = 0m,
                        LifetimeCredit = 0m,
                        LifetimeDebit  = 0m,
                        IsFrozen       = false,
                        Version        = 1,
                        Status         = "active",
                        CreatedAt      = now,
                        UpdatedAt      = now,
                        CreatedBy      = cmd.ActorId
                    };
                    _db.WalletAccounts.Add(wallet);
                    await _db.SaveChangesAsync(ct);
                }

                if (wallet.IsFrozen)
                    throw new BusinessRuleException("Customer wallet is frozen. Cannot credit refund.");

                var balanceBefore = wallet.Balance;
                wallet.Balance         += req.Amount;
                wallet.LifetimeCredit  += req.Amount;
                wallet.LastTransactionAt = now;
                wallet.Version++;
                wallet.UpdatedAt       = now;
                wallet.UpdatedBy       = cmd.ActorId;

                // Append ledger entry (INSERT-only)
                var walletTxn = new WalletTransaction
                {
                    Id              = Guid.NewGuid(),
                    WalletAccountId = wallet.Id,
                    BrandId         = brandId,
                    CustomerId      = payment.CustomerId.Value,
                    TransactionType = "refund_credit",
                    Direction       = 1,
                    Amount          = req.Amount,
                    BalanceBefore   = balanceBefore,
                    BalanceAfter    = wallet.Balance,
                    ReferenceType   = "payment_refund",
                    RefundId        = refund.Id,
                    Description     = $"Refund for payment {payment.PaymentNumber}",
                    PerformedByType = "admin",
                    PerformedById   = cmd.ActorId,
                    IdempotencyKey  = $"refund_{refund.Id}",
                    OccurredAt      = now,
                    CreatedAt       = now,
                    CreatedBy       = cmd.ActorId
                };
                _db.WalletTransactions.Add(walletTxn);
                refund.RefundMethod = "wallet";
            }
            else
            {
                // Gateway refund stub
                if (!string.IsNullOrEmpty(payment.GatewayPaymentId))
                    gatewayRefundId = await _gateway.InitiateRefundAsync(payment.GatewayPaymentId, req.Amount, ct);
                refund.GatewayRefundId = gatewayRefundId;
                refund.RefundMethod    = "gateway";
            }

            refund.Status      = "completed";
            refund.ProcessedAt = now;
            refund.CompletedAt = now;

            _db.PaymentRefunds.Add(refund);
            await _db.SaveChangesAsync(ct);
            await txn.CommitAsync(ct);

            return ToRefundDto(refund);
        }
        catch
        {
            await txn.RollbackAsync(ct);
            throw;
        }
    }

    private static PaymentRefundDto ToRefundDto(PaymentRefund x) => new(
        x.Id, x.BrandId, x.OriginalPaymentId, x.CustomerId, x.RefundNumber,
        x.RefundType, x.Amount, x.Reason, x.ReasonText, x.RefundMethod,
        x.GatewayRefundId, x.Status, x.RequestedAt, x.ProcessedAt, x.CompletedAt,
        x.CreatedAt, x.UpdatedAt);
}
