using commerce.Application.Common.Interfaces;
using FluentValidation;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Exceptions;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Commerce.Partner.Wallet;

// ── Get my wallet (create-on-first-read) ───────────────────────────────────────

/// <summary>Returns the calling partner's prepaid wallet balance. partner_id comes from the
/// tenant context (the partner_id claim); the rls_partner policy independently scopes the row to
/// app.current_partner_id, so a partner can never read another partner's wallet. Create-on-first-
/// read: if the wallet does not exist yet, a zeroed 'active' wallet is created and returned.</summary>
public sealed record GetPartnerWalletQuery : IQuery<PartnerWalletDto>;

public sealed class GetPartnerWalletHandler : IQueryHandler<GetPartnerWalletQuery, PartnerWalletDto>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentTenant _tenant;

    public GetPartnerWalletHandler(ICommerceDbContext db, ICurrentTenant tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<PartnerWalletDto> HandleAsync(GetPartnerWalletQuery q, CancellationToken ct)
    {
        var partnerId = _tenant.PartnerId
            ?? throw new UnauthorizedAccessException("Partner context required.");

        var wallet = await _db.PartnerWalletAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PartnerId == partnerId, ct);
        if (wallet is not null)
            return PartnerWalletMap.ToWalletDto(wallet);

        // Create-on-first-read: every partner has a wallet the moment they look.
        var created = PartnerWalletMap.NewWallet(partnerId, PartnerWalletMap.DefaultCurrency, actorId: null);
        _db.PartnerWalletAccounts.Add(created);
        try
        {
            await _db.SaveChangesAsync(ct);
            // available_balance is DB-generated — reload so the DTO carries it.
            await _db.ReloadAsync(created, ct);
            return PartnerWalletMap.ToWalletDto(created);
        }
        catch (DbUpdateException)
        {
            // Concurrent first-read won the unique(partner_id) race — return the persisted row.
            var raced = await _db.PartnerWalletAccounts.AsNoTracking()
                .FirstAsync(x => x.PartnerId == partnerId, ct);
            return PartnerWalletMap.ToWalletDto(raced);
        }
    }
}

// ── Get my wallet transactions (paged ledger) ──────────────────────────────────

/// <summary>Lists the calling partner's ledger, newest first. Endpoint-gated to PartnerAdmin
/// (docs/rbac.md §13: a partner operator must not see the billing ledger). RLS scopes rows to the
/// caller's partner regardless.</summary>
public sealed record GetPartnerWalletTransactionsQuery(int Page, int PageSize)
    : IQuery<PaginatedList<PartnerWalletTransactionDto>>;

public sealed class GetPartnerWalletTransactionsHandler
    : IQueryHandler<GetPartnerWalletTransactionsQuery, PaginatedList<PartnerWalletTransactionDto>>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentTenant _tenant;

    public GetPartnerWalletTransactionsHandler(ICommerceDbContext db, ICurrentTenant tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public Task<PaginatedList<PartnerWalletTransactionDto>> HandleAsync(
        GetPartnerWalletTransactionsQuery q, CancellationToken ct)
    {
        var partnerId = _tenant.PartnerId
            ?? throw new UnauthorizedAccessException("Partner context required.");

        var query = _db.PartnerWalletTransactions
            .Where(x => x.PartnerId == partnerId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new PartnerWalletTransactionDto(
                x.Id, x.PartnerWalletAccountId, x.PartnerId, x.Direction, x.Amount,
                x.BalanceBefore, x.BalanceAfter, x.ReferenceType, x.ReferenceId,
                x.IdempotencyKey, x.Notes, x.CreatedAt));

        return PaginatedList<PartnerWalletTransactionDto>.CreateAsync(query, q.Page, q.PageSize, ct);
    }
}

// ── Top-up (PartnerAdmin manual/prepaid CREDIT, direction +1) ───────────────────

/// <summary>
/// Idempotent PartnerAdmin prepaid credit. Appends a +1 ledger row (reference_type='topup') and
/// bumps the wallet balance in one transaction. partner_id is taken from the tenant context.
///
/// EXTENSION SEAM (FULL-10, Razorpay payment-link top-up): the gateway-backed flow will verify the
/// captured payment and then call this SAME credit path — reuse <see cref="PartnerWalletLedger"/>
/// with reference_type='topup', reference_id=payment id and idempotency_key=payment id. This wave
/// performs a direct manual credit only; there is no gateway call here.
/// </summary>
public sealed record TopUpPartnerWalletCommand(TopUpPartnerWalletRequest Request, Guid? ActorId)
    : ICommand<PartnerWalletTransactionDto>;

public sealed class TopUpPartnerWalletHandler
    : ICommandHandler<TopUpPartnerWalletCommand, PartnerWalletTransactionDto>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentTenant _tenant;

    public TopUpPartnerWalletHandler(ICommerceDbContext db, ICurrentTenant tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<PartnerWalletTransactionDto> HandleAsync(TopUpPartnerWalletCommand cmd, CancellationToken ct)
    {
        var partnerId = _tenant.PartnerId
            ?? throw new UnauthorizedAccessException("Partner context required.");

        var req = cmd.Request;
        if (req.Amount <= 0)
            throw new BusinessRuleException("Top-up amount must be > 0.");
        if (string.IsNullOrWhiteSpace(req.IdempotencyKey))
            throw new BusinessRuleException("An idempotency key is required for a top-up.");

        var txn = await PartnerWalletLedger.AppendAsync(
            _db,
            partnerId,
            direction: 1,
            amount: req.Amount,
            referenceType: "topup",
            referenceId: null,
            idempotencyKey: req.IdempotencyKey,
            notes: req.Notes,
            currencyCode: PartnerWalletMap.DefaultCurrency,
            actorId: cmd.ActorId,
            ct);

        return PartnerWalletMap.ToTxnDto(txn);
    }
}

public sealed class TopUpPartnerWalletValidator : AbstractValidator<TopUpPartnerWalletRequest>
{
    public TopUpPartnerWalletValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100);
    }
}

// ── Debit (internal — booking consumption, direction -1) ────────────────────────

/// <summary>
/// Internal prepaid DEBIT for a partner booking. Appends a -1 ledger row
/// (reference_type='partner_booking', reference_id=booking id, idempotency_key=booking id) and
/// decrements the wallet balance in one transaction; throws <see cref="BusinessRuleException"/>
/// if the balance is insufficient.
///
/// <see cref="PartnerId"/> is passed EXPLICITLY (not read from the tenant context) because the
/// booking→debit trigger is a CROSS-BC concern (FULL-11): the future wiring runs from the outbox /
/// worker lane (RLS-bypassed) and must target the booking's partner. NOT wired into
/// CreatePartnerBooking in this wave — the command exists so FULL-11 can dispatch it.
/// </summary>
public sealed record DebitPartnerWalletCommand(
    Guid PartnerId,
    decimal Amount,
    Guid BookingId,
    string? Notes,
    Guid? ActorId) : ICommand<PartnerWalletTransactionDto>;

public sealed class DebitPartnerWalletHandler
    : ICommandHandler<DebitPartnerWalletCommand, PartnerWalletTransactionDto>
{
    private readonly ICommerceDbContext _db;

    public DebitPartnerWalletHandler(ICommerceDbContext db) => _db = db;

    public async Task<PartnerWalletTransactionDto> HandleAsync(DebitPartnerWalletCommand cmd, CancellationToken ct)
    {
        if (cmd.PartnerId == Guid.Empty)
            throw new BusinessRuleException("A partner id is required for a debit.");
        if (cmd.Amount <= 0)
            throw new BusinessRuleException("Debit amount must be > 0.");
        if (cmd.BookingId == Guid.Empty)
            throw new BusinessRuleException("A booking id is required for a booking debit.");

        var txn = await PartnerWalletLedger.AppendAsync(
            _db,
            cmd.PartnerId,
            direction: -1,
            amount: cmd.Amount,
            referenceType: "partner_booking",
            referenceId: cmd.BookingId,
            idempotencyKey: cmd.BookingId.ToString("N"),
            notes: cmd.Notes,
            currencyCode: PartnerWalletMap.DefaultCurrency,
            actorId: cmd.ActorId,
            ct);

        return PartnerWalletMap.ToTxnDto(txn);
    }
}

// ── Shared ledger mutation + mappers ────────────────────────────────────────────

/// <summary>The single append-only credit/debit primitive shared by top-up and booking-debit.
/// Idempotent on idempotency_key, atomic (balance mutation + ledger insert in one transaction),
/// and create-on-first-use for the wallet row.</summary>
internal static class PartnerWalletLedger
{
    public static async Task<PartnerWalletTransaction> AppendAsync(
        ICommerceDbContext db,
        Guid partnerId,
        short direction,
        decimal amount,
        string referenceType,
        Guid? referenceId,
        string idempotencyKey,
        string? notes,
        string currencyCode,
        Guid? actorId,
        CancellationToken ct)
    {
        // Idempotency fast path — a replay returns the original row without touching the balance.
        var existing = await db.PartnerWalletTransactions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey && x.PartnerId == partnerId, ct);
        if (existing is not null)
            return existing;

        var now = DateTimeOffset.UtcNow;
        PartnerWalletTransaction txn = null!;

        try
        {
            await db.ExecuteInTransactionAsync(async innerCt =>
            {
                var wallet = await db.PartnerWalletAccounts
                    .FirstOrDefaultAsync(w => w.PartnerId == partnerId, innerCt);

                if (wallet is null)
                {
                    wallet = PartnerWalletMap.NewWallet(partnerId, currencyCode, actorId);
                    db.PartnerWalletAccounts.Add(wallet);
                    await db.SaveChangesAsync(innerCt);
                }

                if (wallet.Status != "active")
                    throw new BusinessRuleException($"Partner wallet is {wallet.Status}; cannot transact.");
                if (direction == -1 && wallet.Balance < amount)
                    throw new BusinessRuleException("Insufficient partner wallet balance.");

                var balanceBefore = wallet.Balance;
                wallet.Balance += direction * amount;
                if (direction == 1) wallet.LifetimeCredit += amount;
                else                wallet.LifetimeDebit  += amount;
                wallet.LastTransactionAt = now;
                wallet.Version++;
                wallet.UpdatedAt = now;
                wallet.UpdatedBy = actorId;

                txn = new PartnerWalletTransaction
                {
                    Id                     = Guid.NewGuid(),
                    PartnerWalletAccountId = wallet.Id,
                    PartnerId              = partnerId,
                    Direction              = direction,
                    Amount                 = amount,
                    BalanceBefore          = balanceBefore,
                    BalanceAfter           = wallet.Balance,
                    ReferenceType          = referenceType,
                    ReferenceId            = referenceId,
                    IdempotencyKey         = idempotencyKey,
                    Notes                  = notes,
                    CreatedAt              = now,
                    CreatedBy              = actorId,
                };
                db.PartnerWalletTransactions.Add(txn);
                await db.SaveChangesAsync(innerCt);
            }, ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent request with the same idempotency_key won the unique-constraint race:
            // the transaction rolled back, so return the row that did commit. Any other write
            // failure (where no such row exists) is re-thrown unchanged.
            var raced = await db.PartnerWalletTransactions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey && x.PartnerId == partnerId, ct);
            if (raced is not null)
                return raced;
            throw;
        }

        return txn;
    }
}

/// <summary>Wallet/ledger entity → DTO mappers + wallet factory (shared across handlers).</summary>
internal static class PartnerWalletMap
{
    public const string DefaultCurrency = "INR";

    public static PartnerWalletAccount NewWallet(Guid partnerId, string currencyCode, Guid? actorId)
    {
        var now = DateTimeOffset.UtcNow;
        return new PartnerWalletAccount
        {
            Id             = Guid.NewGuid(),
            PartnerId      = partnerId,
            CurrencyCode   = currencyCode,
            Balance        = 0m,
            LockedBalance  = 0m,
            LifetimeCredit = 0m,
            LifetimeDebit  = 0m,
            Version        = 1,
            Status         = "active",
            CreatedAt      = now,
            UpdatedAt      = now,
            CreatedBy      = actorId,
        };
    }

    public static PartnerWalletDto ToWalletDto(PartnerWalletAccount x) => new(
        x.Id, x.PartnerId, x.CurrencyCode, x.Balance, x.LockedBalance, x.AvailableBalance,
        x.LifetimeCredit, x.LifetimeDebit, x.LastTransactionAt, x.Version, x.Status,
        x.CreatedAt, x.UpdatedAt);

    public static PartnerWalletTransactionDto ToTxnDto(PartnerWalletTransaction x) => new(
        x.Id, x.PartnerWalletAccountId, x.PartnerId, x.Direction, x.Amount,
        x.BalanceBefore, x.BalanceAfter, x.ReferenceType, x.ReferenceId,
        x.IdempotencyKey, x.Notes, x.CreatedAt);
}
