using laundryghar.Finance.Application.CashBooks.Commands;
using laundryghar.Finance.Application.CashBooks.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Finance.Application.CashBooks.Queries;

// ── List cash books ───────────────────────────────────────────────────────────

public sealed record GetCashBooksQuery(
    int      Page,
    int      PageSize,
    Guid?    StoreId,
    string?  Status,
    DateOnly? BookDate) : IRequest<PaginatedList<CashBookSummaryDto>>;

public sealed class GetCashBooksHandler
    : IRequestHandler<GetCashBooksQuery, PaginatedList<CashBookSummaryDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    public GetCashBooksHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<CashBookSummaryDto>> Handle(GetCashBooksQuery q, CancellationToken ct)
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

public sealed record GetCashBookByIdQuery(Guid Id) : IRequest<CashBookDto?>;

public sealed class GetCashBookByIdHandler : IRequestHandler<GetCashBookByIdQuery, CashBookDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    public GetCashBookByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<CashBookDto?> Handle(GetCashBookByIdQuery q, CancellationToken ct)
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
    : IRequest<PaginatedList<ShiftHandoverDto>>;

public sealed class GetShiftHandoversHandler
    : IRequestHandler<GetShiftHandoversQuery, PaginatedList<ShiftHandoverDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    public GetShiftHandoversHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<ShiftHandoverDto>> Handle(GetShiftHandoversQuery q, CancellationToken ct)
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
