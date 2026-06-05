using laundryghar.Finance.Application.Expenses.Commands;
using laundryghar.Finance.Application.Expenses.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Finance.Application.Expenses.Queries;

// ── List expense categories ───────────────────────────────────────────────────

public sealed record GetExpenseCategoriesQuery(int Page, int PageSize, string? Status)
    : IRequest<PaginatedList<ExpenseCategoryDto>>;

public sealed class GetExpenseCategoriesHandler
    : IRequestHandler<GetExpenseCategoriesQuery, PaginatedList<ExpenseCategoryDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    public GetExpenseCategoriesHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public Task<PaginatedList<ExpenseCategoryDto>> Handle(GetExpenseCategoriesQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.ExpenseCategories.Where(c => c.BrandId == brandId);

        if (!string.IsNullOrEmpty(q.Status)) query = query.Where(c => c.Status == q.Status);

        return PaginatedList<ExpenseCategoryDto>.CreateAsync(
            query.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
                .Select(c => ExpenseMapper.ToCategoryDto(c)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetExpenseCategoryByIdQuery(Guid Id) : IRequest<ExpenseCategoryDto?>;

public sealed class GetExpenseCategoryByIdHandler
    : IRequestHandler<GetExpenseCategoryByIdQuery, ExpenseCategoryDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    public GetExpenseCategoryByIdHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<ExpenseCategoryDto?> Handle(GetExpenseCategoryByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var c = await _db.ExpenseCategories
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return c is null ? null : ExpenseMapper.ToCategoryDto(c);
    }
}

// ── List expenses ─────────────────────────────────────────────────────────────

public sealed record GetExpensesQuery(
    int     Page,
    int     PageSize,
    string? Status,
    Guid?   CategoryId,
    Guid?   StoreId) : IRequest<PaginatedList<ExpenseDto>>;

public sealed class GetExpensesHandler : IRequestHandler<GetExpensesQuery, PaginatedList<ExpenseDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    public GetExpensesHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<ExpenseDto>> Handle(GetExpensesQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.Expenses
            .Include(e => e.Category)
            .Include(e => e.Attachments)
            .Where(e => e.BrandId == brandId);

        if (!string.IsNullOrEmpty(q.Status)) query = query.Where(e => e.Status == q.Status);
        if (q.CategoryId.HasValue) query = query.Where(e => e.CategoryId == q.CategoryId.Value);
        if (q.StoreId.HasValue)    query = query.Where(e => e.StoreId    == q.StoreId.Value);

        return PaginatedList<ExpenseDto>.CreateAsync(
            query.OrderByDescending(e => e.ExpenseDate)
                .Select(e => ExpenseMapper.ToDto(e)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetExpenseByIdQuery(Guid Id) : IRequest<ExpenseDto?>;

public sealed class GetExpenseByIdHandler : IRequestHandler<GetExpenseByIdQuery, ExpenseDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    public GetExpenseByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ExpenseDto?> Handle(GetExpenseByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Expenses
            .Include(x => x.Category)
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : ExpenseMapper.ToDto(e);
    }
}
