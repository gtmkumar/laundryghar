using commerce.Application.Common.Interfaces;
using commerce.Application.Finance.Expenses.Commands;
using commerce.Application.Finance.Expenses.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Finance.Expenses.Queries;

// ── List expense categories ───────────────────────────────────────────────────

public sealed record GetExpenseCategoriesQuery(int Page, int PageSize, string? Status)
    : IQuery<PaginatedList<ExpenseCategoryDto>>;

public sealed class GetExpenseCategoriesHandler
    : IQueryHandler<GetExpenseCategoriesQuery, PaginatedList<ExpenseCategoryDto>>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;
    public GetExpenseCategoriesHandler(ICommerceDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public Task<PaginatedList<ExpenseCategoryDto>> HandleAsync(GetExpenseCategoriesQuery q, CancellationToken ct)
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

public sealed record GetExpenseCategoryByIdQuery(Guid Id) : IQuery<ExpenseCategoryDto?>;

public sealed class GetExpenseCategoryByIdHandler
    : IQueryHandler<GetExpenseCategoryByIdQuery, ExpenseCategoryDto?>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;
    public GetExpenseCategoryByIdHandler(ICommerceDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<ExpenseCategoryDto?> HandleAsync(GetExpenseCategoryByIdQuery q, CancellationToken ct)
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
    Guid?   StoreId) : IQuery<PaginatedList<ExpenseDto>>;

public sealed class GetExpensesHandler : IQueryHandler<GetExpensesQuery, PaginatedList<ExpenseDto>>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;
    public GetExpensesHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<ExpenseDto>> HandleAsync(GetExpensesQuery q, CancellationToken ct)
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

public sealed record GetExpenseByIdQuery(Guid Id) : IQuery<ExpenseDto?>;

public sealed class GetExpenseByIdHandler : IQueryHandler<GetExpenseByIdQuery, ExpenseDto?>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;
    public GetExpenseByIdHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ExpenseDto?> HandleAsync(GetExpenseByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Expenses
            .Include(x => x.Category)
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : ExpenseMapper.ToDto(e);
    }
}
