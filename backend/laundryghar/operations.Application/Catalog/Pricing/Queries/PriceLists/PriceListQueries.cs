using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Pricing.Commands.PriceList;
using operations.Application.Catalog.Pricing.Commands.PriceListItem;
using operations.Application.Catalog.Pricing.Dtos;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Pricing.Queries.PriceLists;

// ── PriceList list/get ────────────────────────────────────────────────────────

public sealed record GetPriceListsQuery(int Page, int PageSize) : IQuery<PaginatedList<PriceListDto>>;

public sealed class GetPriceListsHandler : IQueryHandler<GetPriceListsQuery, PaginatedList<PriceListDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetPriceListsHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<PriceListDto>> HandleAsync(GetPriceListsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        // Brand predicate enforced in-handler (defense-in-depth; RLS also active for non-superuser roles).
        return PaginatedList<PriceListDto>.CreateAsync(
            _db.PriceLists.Where(x => x.BrandId == brandId).OrderByDescending(x => x.CreatedAt)
                .Select(x => CreatePriceListHandler.ToDto(x)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetPriceListByIdQuery(Guid Id) : IQuery<PriceListDto?>;

public sealed class GetPriceListByIdHandler : IQueryHandler<GetPriceListByIdQuery, PriceListDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetPriceListByIdHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PriceListDto?> HandleAsync(GetPriceListByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.PriceLists
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : CreatePriceListHandler.ToDto(e);
    }
}

// ── PriceListItem list ────────────────────────────────────────────────────────

public sealed record GetPriceListItemsQuery(Guid PriceListId, int Page, int PageSize) : IQuery<PaginatedList<PriceListItemDto>>;

public sealed class GetPriceListItemsHandler : IQueryHandler<GetPriceListItemsQuery, PaginatedList<PriceListItemDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetPriceListItemsHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<PriceListItemDto>> HandleAsync(GetPriceListItemsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        return PaginatedList<PriceListItemDto>.CreateAsync(
            _db.PriceListItems
                // Scope by brand_id + priceListId — prevents reading another brand's items.
                .Where(x => x.PriceListId == q.PriceListId && x.BrandId == brandId)
                .OrderBy(x => x.CreatedAt)
                .Select(x => CreatePriceListItemHandler.ToDto(x)),
            q.Page, q.PageSize, ct);
    }
}
