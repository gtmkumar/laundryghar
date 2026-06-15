using commerce.Application.Commerce.Customer.Payments;
using commerce.Application.Common.Interfaces;
using FluentValidation;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Exceptions;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Commerce.Customer.Wallet;

// ── Get my wallet ─────────────────────────────────────────────────────────────

public sealed record GetMyWalletQuery(Guid CustomerId, Guid BrandId) : IQuery<WalletAccountDto?>;

public sealed class GetMyWalletHandler : IQueryHandler<GetMyWalletQuery, WalletAccountDto?>
{
    private readonly ICommerceDbContext _db;

    public GetMyWalletHandler(ICommerceDbContext db) => _db = db;

    public async Task<WalletAccountDto?> HandleAsync(GetMyWalletQuery q, CancellationToken ct)
    {
        var e = await _db.WalletAccounts
            .FirstOrDefaultAsync(x => x.CustomerId == q.CustomerId && x.BrandId == q.BrandId, ct);
        return e is null ? null : ToWalletDto(e);
    }

    internal static WalletAccountDto ToWalletDto(WalletAccount x) => new(
        x.Id, x.BrandId, x.CustomerId, x.CurrencyCode, x.Balance, x.LockedBalance,
        x.AvailableBalance, x.LifetimeCredit, x.LifetimeDebit, x.LastTransactionAt,
        x.IsFrozen, x.Version, x.Status, x.CreatedAt, x.UpdatedAt);
}

// ── Get my wallet transactions ────────────────────────────────────────────────

public sealed record GetMyWalletTransactionsQuery(Guid CustomerId, Guid BrandId, int Page, int PageSize)
    : IQuery<PaginatedList<WalletTransactionDto>>;

public sealed class GetMyWalletTransactionsHandler : IQueryHandler<GetMyWalletTransactionsQuery, PaginatedList<WalletTransactionDto>>
{
    private readonly ICommerceDbContext _db;

    public GetMyWalletTransactionsHandler(ICommerceDbContext db) => _db = db;

    public Task<PaginatedList<WalletTransactionDto>> HandleAsync(GetMyWalletTransactionsQuery q, CancellationToken ct)
    {
        var query = _db.WalletTransactions
            .Where(x => x.CustomerId == q.CustomerId && x.BrandId == q.BrandId)
            .OrderByDescending(x => x.OccurredAt)
            .Select(x => new WalletTransactionDto(
                x.Id, x.WalletAccountId, x.BrandId, x.CustomerId,
                x.TransactionType, x.Direction, x.Amount,
                x.BalanceBefore, x.BalanceAfter, x.ReferenceType, x.ReferenceId,
                x.Description, x.Notes, x.IdempotencyKey, x.OccurredAt, x.CreatedAt));
        return PaginatedList<WalletTransactionDto>.CreateAsync(query, q.Page, q.PageSize, ct);
    }
}

// ── Wallet top-up: initiate payment ──────────────────────────────────────────

public sealed record WalletTopUpInitiateCommand(
    Guid CustomerId,
    Guid BrandId,
    WalletTopUpRequest Request,
    string IdempotencyKey
) : ICommand<PaymentDto>;

public sealed class WalletTopUpInitiateHandler : ICommandHandler<WalletTopUpInitiateCommand, PaymentDto>
{
    private readonly ICommerceDbContext _db;
    private readonly IPaymentGateway _gateway;

    public WalletTopUpInitiateHandler(ICommerceDbContext db, IPaymentGateway gateway)
    {
        _db = db;
        _gateway = gateway;
    }

    public async Task<PaymentDto> HandleAsync(WalletTopUpInitiateCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;
        if (req.Amount <= 0)
            throw new BusinessRuleException("Top-up amount must be > 0.");

        var handler = new InitiatePaymentHandler(_db, _gateway);
        return await handler.HandleAsync(new InitiatePaymentCommand(
            cmd.CustomerId,
            cmd.BrandId,
            new InitiatePaymentRequest(req.Amount, req.CurrencyCode, "wallet_topup", req.PaymentMethodId, null, null, req.Notes),
            cmd.IdempotencyKey), ct);
    }
}

public sealed class WalletTopUpInitiateValidator : AbstractValidator<WalletTopUpRequest>
{
    public WalletTopUpInitiateValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
    }
}

// ── Wallet top-up: verify → credit wallet ─────────────────────────────────────

/// <summary>
/// On gateway verify success: appends wallet_transactions credit row AND updates
/// wallet_accounts.balance in ONE transaction. Idempotent: if a wallet_transaction
/// with the same idempotency_key exists, returns the existing transaction.
/// </summary>
public sealed record WalletTopUpVerifyCommand(
    Guid CustomerId,
    Guid BrandId,
    VerifyPaymentRequest Request
) : ICommand<WalletTransactionDto>;

public sealed class WalletTopUpVerifyHandler : ICommandHandler<WalletTopUpVerifyCommand, WalletTransactionDto>
{
    private readonly ICommerceDbContext _db;
    private readonly IPaymentGateway _gateway;

    public WalletTopUpVerifyHandler(ICommerceDbContext db, IPaymentGateway gateway)
    {
        _db = db;
        _gateway = gateway;
    }

    public async Task<WalletTransactionDto> HandleAsync(WalletTopUpVerifyCommand cmd, CancellationToken ct)
    {
        // Verify the payment
        var verifyHandler = new VerifyPaymentHandler(_db, _gateway);
        var paymentDto = await verifyHandler.HandleAsync(
            new VerifyPaymentCommand(cmd.CustomerId, cmd.BrandId, cmd.Request), ct);

        var payment = await _db.Payments.FirstAsync(p => p.Id == paymentDto.Id, ct);

        // Idempotency: check if wallet was already credited for this payment
        var idempotencyKey = $"topup_{payment.Id}";
        var existingTxn = await _db.WalletTransactions
            .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, ct);
        if (existingTxn is not null)
            return ToWalletTxnDto(existingTxn);

        var now = DateTimeOffset.UtcNow;

        WalletTransaction walletTxn = null!;

        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            // Ensure wallet exists
            var wallet = await _db.WalletAccounts
                .FirstOrDefaultAsync(w => w.CustomerId == cmd.CustomerId && w.BrandId == cmd.BrandId, innerCt);

            if (wallet is null)
            {
                wallet = new WalletAccount
                {
                    Id             = Guid.NewGuid(),
                    BrandId        = cmd.BrandId,
                    CustomerId     = cmd.CustomerId,
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
                    CreatedBy      = cmd.CustomerId
                };
                _db.WalletAccounts.Add(wallet);
                await _db.SaveChangesAsync(innerCt);
            }

            if (wallet.IsFrozen)
                throw new BusinessRuleException("Wallet is frozen. Cannot credit top-up.");

            var balanceBefore   = wallet.Balance;
            wallet.Balance         += payment.Amount;
            wallet.LifetimeCredit  += payment.Amount;
            wallet.LastTransactionAt = now;
            wallet.Version++;
            wallet.UpdatedAt       = now;
            wallet.UpdatedBy       = cmd.CustomerId;

            // Append-only ledger INSERT
            walletTxn = new WalletTransaction
            {
                Id              = Guid.NewGuid(),
                WalletAccountId = wallet.Id,
                BrandId         = cmd.BrandId,
                CustomerId      = cmd.CustomerId,
                TransactionType = "topup",
                Direction       = 1,
                Amount          = payment.Amount,
                BalanceBefore   = balanceBefore,
                BalanceAfter    = wallet.Balance,
                ReferenceType   = "payment",
                ReferenceId     = payment.Id,
                PaymentId       = payment.Id,
                Description     = "Wallet top-up",
                PerformedByType = "customer",
                PerformedById   = cmd.CustomerId,
                IdempotencyKey  = idempotencyKey,
                OccurredAt      = now,
                CreatedAt       = now,
                CreatedBy       = cmd.CustomerId
            };
            _db.WalletTransactions.Add(walletTxn);
            await _db.SaveChangesAsync(innerCt);
        }, ct);

        return ToWalletTxnDto(walletTxn);
    }

    private static WalletTransactionDto ToWalletTxnDto(WalletTransaction x) => new(
        x.Id, x.WalletAccountId, x.BrandId, x.CustomerId, x.TransactionType,
        x.Direction, x.Amount, x.BalanceBefore, x.BalanceAfter,
        x.ReferenceType, x.ReferenceId, x.Description, x.Notes,
        x.IdempotencyKey, x.OccurredAt, x.CreatedAt);
}
