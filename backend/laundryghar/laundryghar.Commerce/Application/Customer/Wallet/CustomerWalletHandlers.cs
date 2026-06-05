using FluentValidation;
using laundryghar.Commerce.Application;
using laundryghar.Commerce.Application.Customer.Payments;
using laundryghar.Commerce.Infrastructure.Gateway;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Commerce.Application.Customer.Wallet;

// ── Get my wallet ─────────────────────────────────────────────────────────────

public sealed record GetMyWalletQuery(Guid CustomerId, Guid BrandId) : IRequest<WalletAccountDto?>;

public sealed class GetMyWalletHandler : IRequestHandler<GetMyWalletQuery, WalletAccountDto?>
{
    private readonly LaundryGharDbContext _db;

    public GetMyWalletHandler(LaundryGharDbContext db) => _db = db;

    public async Task<WalletAccountDto?> Handle(GetMyWalletQuery q, CancellationToken ct)
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
    : IRequest<PaginatedList<WalletTransactionDto>>;

public sealed class GetMyWalletTransactionsHandler : IRequestHandler<GetMyWalletTransactionsQuery, PaginatedList<WalletTransactionDto>>
{
    private readonly LaundryGharDbContext _db;

    public GetMyWalletTransactionsHandler(LaundryGharDbContext db) => _db = db;

    public Task<PaginatedList<WalletTransactionDto>> Handle(GetMyWalletTransactionsQuery q, CancellationToken ct)
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
) : IRequest<PaymentDto>;

public sealed class WalletTopUpInitiateHandler : IRequestHandler<WalletTopUpInitiateCommand, PaymentDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly IPaymentGateway _gateway;

    public WalletTopUpInitiateHandler(LaundryGharDbContext db, IPaymentGateway gateway)
    {
        _db = db;
        _gateway = gateway;
    }

    public async Task<PaymentDto> Handle(WalletTopUpInitiateCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;
        if (req.Amount <= 0)
            throw new BusinessRuleException("Top-up amount must be > 0.");

        var handler = new InitiatePaymentHandler(_db, _gateway);
        return await handler.Handle(new InitiatePaymentCommand(
            cmd.CustomerId,
            cmd.BrandId,
            new InitiatePaymentRequest(req.Amount, req.CurrencyCode, "wallet_topup", req.PaymentMethodId, null, null, req.Notes),
            cmd.IdempotencyKey), ct);
    }
}

public sealed class WalletTopUpInitiateValidator : AbstractValidator<WalletTopUpInitiateCommand>
{
    public WalletTopUpInitiateValidator()
    {
        RuleFor(x => x.Request.Amount).GreaterThan(0);
        RuleFor(x => x.Request.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.IdempotencyKey).NotEmpty();
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
) : IRequest<WalletTransactionDto>;

public sealed class WalletTopUpVerifyHandler : IRequestHandler<WalletTopUpVerifyCommand, WalletTransactionDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly IPaymentGateway _gateway;

    public WalletTopUpVerifyHandler(LaundryGharDbContext db, IPaymentGateway gateway)
    {
        _db = db;
        _gateway = gateway;
    }

    public async Task<WalletTransactionDto> Handle(WalletTopUpVerifyCommand cmd, CancellationToken ct)
    {
        // Verify the payment
        var verifyHandler = new VerifyPaymentHandler(_db, _gateway);
        var paymentDto = await verifyHandler.Handle(
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

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var txn = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // Ensure wallet exists
                var wallet = await _db.WalletAccounts
                    .FirstOrDefaultAsync(w => w.CustomerId == cmd.CustomerId && w.BrandId == cmd.BrandId, ct);

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
                    await _db.SaveChangesAsync(ct);
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
                await _db.SaveChangesAsync(ct);
                await txn.CommitAsync(ct);
            }
            catch
            {
                await txn.RollbackAsync(ct);
                throw;
            }
        });

        return ToWalletTxnDto(walletTxn);
    }

    private static WalletTransactionDto ToWalletTxnDto(WalletTransaction x) => new(
        x.Id, x.WalletAccountId, x.BrandId, x.CustomerId, x.TransactionType,
        x.Direction, x.Amount, x.BalanceBefore, x.BalanceAfter,
        x.ReferenceType, x.ReferenceId, x.Description, x.Notes,
        x.IdempotencyKey, x.OccurredAt, x.CreatedAt);
}
