using FluentValidation;
using laundryghar.Commerce.Application;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Commerce.Application.Admin.Wallet;

// ── Queries ───────────────────────────────────────────────────────────────────

public sealed record GetCustomerWalletQuery(Guid CustomerId) : IRequest<WalletAccountDto?>;

public sealed class GetCustomerWalletHandler : IRequestHandler<GetCustomerWalletQuery, WalletAccountDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetCustomerWalletHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<WalletAccountDto?> Handle(GetCustomerWalletQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.WalletAccounts
            .FirstOrDefaultAsync(x => x.CustomerId == q.CustomerId && x.BrandId == brandId, ct);
        return e is null ? null : ToWalletDto(e);
    }

    internal static WalletAccountDto ToWalletDto(WalletAccount x) => new(
        x.Id, x.BrandId, x.CustomerId, x.CurrencyCode, x.Balance, x.LockedBalance,
        x.AvailableBalance, x.LifetimeCredit, x.LifetimeDebit, x.LastTransactionAt,
        x.IsFrozen, x.Version, x.Status, x.CreatedAt, x.UpdatedAt);
}

public sealed record GetCustomerWalletTransactionsQuery(Guid CustomerId, int Page, int PageSize) : IRequest<PaginatedList<WalletTransactionDto>>;

public sealed class GetCustomerWalletTransactionsHandler : IRequestHandler<GetCustomerWalletTransactionsQuery, PaginatedList<WalletTransactionDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetCustomerWalletTransactionsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<WalletTransactionDto>> Handle(GetCustomerWalletTransactionsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query = _db.WalletTransactions
            .Where(x => x.CustomerId == q.CustomerId && x.BrandId == brandId)
            .OrderByDescending(x => x.OccurredAt)
            .Select(x => ToTransactionDto(x));
        return PaginatedList<WalletTransactionDto>.CreateAsync(query, q.Page, q.PageSize, ct);
    }

    internal static WalletTransactionDto ToTransactionDto(WalletTransaction x) => new(
        x.Id, x.WalletAccountId, x.BrandId, x.CustomerId, x.TransactionType, x.Direction,
        x.Amount, x.BalanceBefore, x.BalanceAfter, x.ReferenceType, x.ReferenceId,
        x.Description, x.Notes, x.IdempotencyKey, x.OccurredAt, x.CreatedAt);
}

// ── Admin Adjustment ──────────────────────────────────────────────────────────

/// <summary>
/// Idempotent admin wallet adjustment.
/// If IdempotencyKey already exists in wallet_transactions, returns the original result.
/// Otherwise: appends a wallet_transactions row AND updates wallet_accounts.balance in one transaction.
/// </summary>
public sealed record AdminWalletAdjustCommand(AdminWalletAdjustRequest Request, Guid? ActorId) : IRequest<WalletTransactionDto>;

public sealed class AdminWalletAdjustHandler : IRequestHandler<AdminWalletAdjustCommand, WalletTransactionDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public AdminWalletAdjustHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<WalletTransactionDto> Handle(AdminWalletAdjustCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;

        if (req.Direction != 1 && req.Direction != -1)
            throw new BusinessRuleException("Direction must be 1 (credit) or -1 (debit).");
        if (req.Amount <= 0)
            throw new BusinessRuleException("Amount must be > 0.");

        // Idempotency check
        if (!string.IsNullOrWhiteSpace(req.IdempotencyKey))
        {
            var existing = await _db.WalletTransactions
                .FirstOrDefaultAsync(x => x.IdempotencyKey == req.IdempotencyKey && x.BrandId == brandId, ct);
            if (existing is not null)
                return GetCustomerWalletTransactionsHandler.ToTransactionDto(existing);
        }

        var now = DateTimeOffset.UtcNow;
        var idempotencyKey = req.IdempotencyKey ?? $"adj_{brandId}_{req.CustomerId}_{Guid.NewGuid():N}";

        WalletTransaction walletTxn = null!;

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var txn = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var wallet = await _db.WalletAccounts
                    .FirstOrDefaultAsync(w => w.CustomerId == req.CustomerId && w.BrandId == brandId, ct);

                if (wallet is null)
                {
                    wallet = new WalletAccount
                    {
                        Id             = Guid.NewGuid(),
                        BrandId        = brandId,
                        CustomerId     = req.CustomerId,
                        CurrencyCode   = "INR",
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
                    throw new BusinessRuleException("Wallet is frozen. Cannot adjust.");
                if (req.Direction == -1 && wallet.Balance < req.Amount)
                    throw new BusinessRuleException("Insufficient wallet balance for debit adjustment.");

                var balanceBefore = wallet.Balance;
                wallet.Balance += req.Direction * req.Amount;

                if (req.Direction == 1)
                    wallet.LifetimeCredit += req.Amount;
                else
                    wallet.LifetimeDebit += req.Amount;

                wallet.LastTransactionAt = now;
                wallet.Version++;
                wallet.UpdatedAt = now;
                wallet.UpdatedBy = cmd.ActorId;

                // Append-only ledger INSERT
                walletTxn = new WalletTransaction
                {
                    Id              = Guid.NewGuid(),
                    WalletAccountId = wallet.Id,
                    BrandId         = brandId,
                    CustomerId      = req.CustomerId,
                    TransactionType = req.TransactionType,
                    Direction       = req.Direction,
                    Amount          = req.Amount,
                    BalanceBefore   = balanceBefore,
                    BalanceAfter    = wallet.Balance,
                    Description     = req.Description,
                    Notes           = req.Notes,
                    PerformedByType = "admin",
                    PerformedById   = cmd.ActorId,
                    IdempotencyKey  = idempotencyKey,
                    OccurredAt      = now,
                    CreatedAt       = now,
                    CreatedBy       = cmd.ActorId
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

        return GetCustomerWalletTransactionsHandler.ToTransactionDto(walletTxn);
    }
}

public sealed class AdminWalletAdjustValidator : AbstractValidator<AdminWalletAdjustCommand>
{
    public AdminWalletAdjustValidator()
    {
        RuleFor(x => x.Request.CustomerId).NotEmpty();
        RuleFor(x => x.Request.Amount).GreaterThan(0);
        RuleFor(x => x.Request.Direction).Must(d => d == 1 || d == -1)
            .WithMessage("Direction must be 1 (credit) or -1 (debit).");
        RuleFor(x => x.Request.TransactionType).NotEmpty();
    }
}
