using commerce.Application.Common.Interfaces;
using commerce.Application.Finance.CashBooks.Commands;
using commerce.Application.Finance.CashBooks.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Finance.CashBooks.Queries;

// ── List cash books ───────────────────────────────────────────────────────────

public sealed record GetCashBooksQuery(
    int      Page,
    int      PageSize,
    Guid?    StoreId,
    string?  Status,
    DateOnly? BookDate) : IQuery<PaginatedList<CashBookSummaryDto>>;

public sealed class GetCashBooksHandler
    : IQueryHandler<GetCashBooksQuery, PaginatedList<CashBookSummaryDto>>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;
    public GetCashBooksHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<CashBookSummaryDto>> HandleAsync(GetCashBooksQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.CashBooks.Where(b => b.BrandId == brandId);

        if (q.StoreId.HasValue)  query = query.Where(b => b.StoreId  == q.StoreId.Value);
        if (!string.IsNullOrEmpty(q.Status)) query = query.Where(b => b.Status == q.Status);
        if (q.BookDate.HasValue) query = query.Where(b => b.BookDate == q.BookDate.Value);

        return PaginatedList<CashBookSummaryDto>.CreateAsync(
            query.OrderByDescending(b => b.BookDate).ThenByDescending(b => b.OpenedAt)
                .Select(b => new CashBookSummaryDto(
                    b.Id, b.StoreId, b.BookDate, b.ShiftLabel,
                    b.OpeningBalance, b.ClosingBalance, b.Variance,
                    b.CashInflow, b.CashOutflow, b.Status,
                    b.OpenedAt, b.ClosedAt)),
            q.Page, q.PageSize, ct);
    }
}

// ── Get cash book by id (with entries) ───────────────────────────────────────

public sealed record GetCashBookByIdQuery(Guid Id) : IQuery<CashBookDto?>;

public sealed class GetCashBookByIdHandler : IQueryHandler<GetCashBookByIdQuery, CashBookDto?>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;
    public GetCashBookByIdHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<CashBookDto?> HandleAsync(GetCashBookByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var book = await _db.CashBooks
            .Include(b => b.Entries)
            .FirstOrDefaultAsync(b => b.Id == q.Id && b.BrandId == brandId, ct);
        return book is null ? null : CashBookMapper.ToDto(book);
    }
}

// ── List shift handovers ──────────────────────────────────────────────────────

public sealed record GetShiftHandoversQuery(int Page, int PageSize, Guid? StoreId, string? Status)
    : IQuery<PaginatedList<ShiftHandoverDto>>;

public sealed class GetShiftHandoversHandler
    : IQueryHandler<GetShiftHandoversQuery, PaginatedList<ShiftHandoverDto>>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;
    public GetShiftHandoversHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<ShiftHandoverDto>> HandleAsync(GetShiftHandoversQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.ShiftHandovers.Where(h => h.BrandId == brandId);

        if (q.StoreId.HasValue) query = query.Where(h => h.StoreId == q.StoreId.Value);
        if (!string.IsNullOrEmpty(q.Status)) query = query.Where(h => h.Status == q.Status);

        return PaginatedList<ShiftHandoverDto>.CreateAsync(
            query.OrderByDescending(h => h.HandoverAt)
                .Select(h => CashBookMapper.HandoverToDto(h)),
            q.Page, q.PageSize, ct);
    }
}
