using commerce.Application.Common.Interfaces;
using commerce.Application.Finance.CashBooks.Dtos;
using FluentValidation;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using laundryghar.Utilities.Exceptions;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Finance.CashBooks.Commands;

// ── Shared mapping ────────────────────────────────────────────────────────────

internal static class CashBookMapper
{
    internal static CashBookDto ToDto(CashBook b, IEnumerable<CashBookEntry>? entries = null) => new(
        b.Id, b.BrandId, b.FranchiseId, b.StoreId,
        b.BookDate, b.ShiftLabel,
        b.OpeningBalance, b.ClosingBalance, b.ExpectedClosing, b.Variance,
        b.CashInflow, b.CashOutflow, b.UpiInflow, b.CardInflow, b.OtherInflow, b.DepositAmount,
        b.TotalOrders, b.Status, b.Notes,
        b.OpenedAt, b.ClosedAt, b.CreatedAt,
        (entries ?? b.Entries).Select(EntryToDto).ToList());

    internal static CashBookEntryDto EntryToDto(CashBookEntry e) => new(
        e.Id, e.CashBookId, e.EntryType, e.Category,
        e.Direction, e.Amount, e.PaymentMode,
        e.Description, e.PayeeName, e.ReceiptNumber,
        e.ExpenseId, e.OccurredAt, e.CreatedAt);

    internal static ShiftHandoverDto HandoverToDto(ShiftHandover h) => new(
        h.Id, h.StoreId, h.FromUserId, h.ToUserId, h.CashBookId,
        h.HandoverAt, h.CashHandedOver, h.CashVariance,
        h.Status, h.NotesFrom, h.CreatedAt);
}

// ── Open CashBook ─────────────────────────────────────────────────────────────

public sealed record OpenCashBookCommand(OpenCashBookRequest Request, Guid? ActorId)
    : ICommand<CashBookDto>;

public sealed class OpenCashBookHandler : ICommandHandler<OpenCashBookCommand, CashBookDto>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;

    public OpenCashBookHandler(ICommerceDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<CashBookDto> HandleAsync(OpenCashBookCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        // Validate the store belongs to this brand (cross-brand IDOR guard).
        var storeInBrand = await _db.Stores
            .AnyAsync(s => s.Id == req.StoreId && s.BrandId == brandId, ct);
        if (!storeInBrand)
            throw new KeyNotFoundException("Store not found.");

        // Validate the franchise belongs to this brand.
        var franchiseInBrand = await _db.Franchises
            .AnyAsync(f => f.Id == req.FranchiseId && f.BrandId == brandId, ct);
        if (!franchiseInBrand)
            throw new KeyNotFoundException("Franchise not found.");

        // Enforce uniqueness (store + date + shift)
        var exists = await _db.CashBooks.AnyAsync(
            b => b.BrandId   == brandId
              && b.StoreId   == req.StoreId
              && b.BookDate  == req.BookDate
              && b.ShiftLabel == req.ShiftLabel, ct);

        if (exists)
            throw new BusinessRuleException(
                $"A cash book already exists for store {req.StoreId} on {req.BookDate} ({req.ShiftLabel}).");

        var book = new CashBook
        {
            Id              = Guid.NewGuid(),
            BrandId         = brandId,
            FranchiseId     = req.FranchiseId,
            StoreId         = req.StoreId,
            BookDate        = req.BookDate,
            ShiftLabel      = req.ShiftLabel,
            OpeningUserId   = cmd.ActorId ?? Guid.Empty,
            OpeningBalance  = req.OpeningBalance,
            CashInflow      = 0,
            CashOutflow     = 0,
            UpiInflow       = 0,
            CardInflow      = 0,
            OtherInflow     = 0,
            DepositAmount   = 0,
            TotalOrders     = 0,
            NewOrders       = 0,
            DeliveredOrders = 0,
            CancelledOrders = 0,
            Status          = "open",
            Metadata        = "{}",
            OpenedAt        = now,
            CreatedAt       = now,
            UpdatedAt       = now,
            CreatedBy       = cmd.ActorId,
            UpdatedBy       = cmd.ActorId
        };

        _db.CashBooks.Add(book);
        await _db.SaveChangesAsync(ct);
        return CashBookMapper.ToDto(book, []);
    }
}

public sealed class OpenCashBookValidator : AbstractValidator<OpenCashBookRequest>
{
    private static readonly string[] ValidShifts =
        ["morning", "afternoon", "evening", "night", "full_day"];

    public OpenCashBookValidator()
    {
        RuleFor(x => x.StoreId).NotEmpty();
        RuleFor(x => x.FranchiseId).NotEmpty();
        RuleFor(x => x.BookDate).NotEqual(default(DateOnly));
        RuleFor(x => x.ShiftLabel)
            .Must(s => ValidShifts.Contains(s))
            .WithMessage($"ShiftLabel must be one of: {string.Join(", ", ValidShifts)}.");
        RuleFor(x => x.OpeningBalance).GreaterThanOrEqualTo(0);
    }
}

// ── Add CashBook Entry ────────────────────────────────────────────────────────

public sealed record AddCashBookEntryCommand(Guid BookId, AddCashBookEntryRequest Request, Guid? ActorId)
    : ICommand<CashBookDto>;

public sealed class AddCashBookEntryHandler : ICommandHandler<AddCashBookEntryCommand, CashBookDto>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;

    public AddCashBookEntryHandler(ICommerceDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<CashBookDto> HandleAsync(AddCashBookEntryCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;

        CashBookDto result = null!;

        // The retrying execution strategy owns the transaction boundary (ExecuteInTransactionAsync).
        await _db.ExecuteInTransactionAsync(async token =>
        {
            var now  = DateTimeOffset.UtcNow;
            var book = await _db.CashBooks
                .Include(b => b.Entries)
                .FirstOrDefaultAsync(b => b.Id == cmd.BookId && b.BrandId == brandId, token);

            if (book is null)
                throw new BusinessRuleException("Cash book not found.");
            if (book.Status != "open")
                throw new BusinessRuleException($"Cannot add entries to a cash book with status '{book.Status}'.");

            var entry = new CashBookEntry
            {
                Id            = Guid.NewGuid(),
                CashBookId    = book.Id,
                BrandId       = brandId,
                StoreId       = book.StoreId,
                EntryType     = req.EntryType,
                Category      = req.Category,
                Direction     = req.Direction,
                Amount        = req.Amount,
                PaymentMode   = req.PaymentMode,
                Description   = req.Description,
                PayeeName     = req.PayeeName,
                ReceiptNumber = req.ReceiptNumber,
                ExpenseId     = req.ExpenseId,
                PerformedBy   = cmd.ActorId ?? Guid.Empty,
                OccurredAt    = now,
                Metadata      = "{}",
                CreatedAt     = now,
                CreatedBy     = cmd.ActorId
            };

            _db.CashBookEntries.Add(entry);

            UpdateRunningTotals(book, req);
            book.UpdatedAt = now;
            book.UpdatedBy = cmd.ActorId;

            await _db.SaveChangesAsync(token);

            // EF relationship fix-up already added the tracked new entry to book.Entries
            // (the book was loaded with .Include(b => b.Entries)). Adding it again here
            // would duplicate it in the response aggregate, so we don't. Map the
            // already-correct tracked collection.
            result = CashBookMapper.ToDto(book);
        }, ct);

        return result;
    }

    private static void UpdateRunningTotals(CashBook book, AddCashBookEntryRequest req)
    {
        if (req.Direction == 1)
        {
            // Inflow
            switch (req.PaymentMode)
            {
                case "upi":          book.UpiInflow   += req.Amount; break;
                case "card":         book.CardInflow  += req.Amount; break;
                case "cash":         book.CashInflow  += req.Amount; break;
                default:             book.OtherInflow += req.Amount; break;
            }
        }
        else
        {
            // Outflow (direction == -1)
            book.CashOutflow += req.Amount;
        }

        if (req.EntryType == "deposit")
            book.DepositAmount += req.Amount;
    }
}

public sealed class AddCashBookEntryValidator : AbstractValidator<AddCashBookEntryRequest>
{
    private static readonly string[] ValidEntryTypes =
        ["cash_in", "cash_out", "deposit", "withdrawal", "adjustment", "opening", "closing"];
    private static readonly string[] ValidCategories =
        ["order_payment", "refund", "expense", "salary", "utility", "rent",
         "maintenance", "supply", "tip", "adjustment", "deposit", "other"];
    private static readonly string[] ValidPaymentModes =
        ["cash", "upi", "card", "bank_transfer", "other"];

    public AddCashBookEntryValidator()
    {
        RuleFor(x => x.EntryType)
            .Must(t => ValidEntryTypes.Contains(t))
            .WithMessage($"EntryType must be one of: {string.Join(", ", ValidEntryTypes)}.");
        RuleFor(x => x.Category)
            .Must(c => ValidCategories.Contains(c))
            .WithMessage($"Category must be one of: {string.Join(", ", ValidCategories)}.");
        RuleFor(x => x.Direction)
            .Must(d => d == 1 || d == -1)
            .WithMessage("Direction must be 1 (in) or -1 (out).");
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.PaymentMode)
            .Must(p => ValidPaymentModes.Contains(p))
            .WithMessage($"PaymentMode must be one of: {string.Join(", ", ValidPaymentModes)}.");
    }
}

// ── Close CashBook ────────────────────────────────────────────────────────────

public sealed record CloseCashBookCommand(Guid BookId, CloseCashBookRequest Request, Guid? ActorId)
    : ICommand<CashBookDto>;

public sealed class CloseCashBookHandler : ICommandHandler<CloseCashBookCommand, CashBookDto>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;

    public CloseCashBookHandler(ICommerceDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<CashBookDto> HandleAsync(CloseCashBookCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        var book = await _db.CashBooks
            .Include(b => b.Entries)
            .FirstOrDefaultAsync(b => b.Id == cmd.BookId && b.BrandId == brandId, ct);

        if (book is null)
            throw new BusinessRuleException("Cash book not found.");
        if (book.Status != "open")
            throw new BusinessRuleException($"Cannot close a cash book with status '{book.Status}'.");

        // Compute expected closing: opening_balance + net inflow - outflow
        var netInflow = book.CashInflow + book.UpiInflow + book.CardInflow + book.OtherInflow;
        var expected  = book.OpeningBalance + netInflow - book.CashOutflow;

        book.ClosingBalance  = req.ClosingBalance;
        book.ExpectedClosing = expected;
        // Variance is a generated column — DB computes it; we do NOT set it here.
        book.ClosingUserId   = cmd.ActorId;
        book.ClosedAt        = now;
        book.Status          = "closed";
        book.VarianceReason  = req.VarianceReason;
        book.Notes           = req.Notes;
        book.UpdatedAt       = now;
        book.UpdatedBy       = cmd.ActorId;

        await _db.SaveChangesAsync(ct);

        // Reload to get DB-computed variance
        await _db.ReloadAsync(book, ct);

        return CashBookMapper.ToDto(book);
    }
}

public sealed class CloseCashBookValidator : AbstractValidator<CloseCashBookRequest>
{
    public CloseCashBookValidator()
    {
        // Closing balance can be zero (empty float) but must not be negative.
        RuleFor(x => x.ClosingBalance)
            .GreaterThanOrEqualTo(0)
            .WithMessage("ClosingBalance must be zero or greater.");
        RuleFor(x => x.VarianceReason)
            .MaximumLength(500)
            .When(x => x.VarianceReason is not null);
    }
}

// ── Create Shift Handover ─────────────────────────────────────────────────────

public sealed record CreateShiftHandoverCommand(CreateShiftHandoverRequest Request, Guid? ActorId)
    : ICommand<ShiftHandoverDto>;

public sealed class CreateShiftHandoverHandler : ICommandHandler<CreateShiftHandoverCommand, ShiftHandoverDto>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;

    public CreateShiftHandoverHandler(ICommerceDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<ShiftHandoverDto> HandleAsync(CreateShiftHandoverCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        // Cross-brand guards: the store and the (optional) cash book being
        // handed over must belong to this brand.
        var storeExists = await _db.Stores
            .AnyAsync(s => s.Id == req.StoreId && s.BrandId == brandId, ct);
        if (!storeExists)
            throw new KeyNotFoundException("Store not found.");
        if (req.CashBookId is Guid cashBookId)
        {
            var bookExists = await _db.CashBooks
                .AnyAsync(b => b.Id == cashBookId && b.BrandId == brandId, ct);
            if (!bookExists)
                throw new KeyNotFoundException("Cash book not found.");
        }

        var handover = new ShiftHandover
        {
            Id                   = Guid.NewGuid(),
            BrandId              = brandId,
            StoreId              = req.StoreId,
            FromUserId           = req.FromUserId,
            ToUserId             = req.ToUserId,
            CashBookId           = req.CashBookId,
            HandoverAt           = now,
            CashHandedOver       = req.CashHandedOver,
            PendingOrdersCount   = req.PendingOrdersCount,
            OpenComplaintsCount  = req.OpenComplaintsCount,
            PickupsRemaining     = req.PickupsRemaining,
            DeliveriesRemaining  = req.DeliveriesRemaining,
            NotesFrom            = req.NotesFrom,
            PendingItems         = "[]",
            Status               = "pending",
            CreatedAt            = now,
            CreatedBy            = cmd.ActorId
        };

        _db.ShiftHandovers.Add(handover);
        await _db.SaveChangesAsync(ct);

        // Reload for DB-computed cash_variance
        await _db.ReloadAsync(handover, ct);

        return CashBookMapper.HandoverToDto(handover);
    }
}

public sealed class CreateShiftHandoverValidator : AbstractValidator<CreateShiftHandoverRequest>
{
    public CreateShiftHandoverValidator()
    {
        RuleFor(x => x.StoreId).NotEmpty();
        RuleFor(x => x.FromUserId).NotEmpty();
        // CashHandedOver may be 0 for non-cash shifts but never negative.
        RuleFor(x => x.CashHandedOver)
            .GreaterThanOrEqualTo(0)
            .WithMessage("CashHandedOver must be zero or greater.");
        RuleFor(x => x.PendingOrdersCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PickupsRemaining).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DeliveriesRemaining).GreaterThanOrEqualTo(0);
    }
}
