using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.StockReconciliation.Commands.CreateStockRecon;
using operations.Application.Warehouse.StockReconciliation.Dtos;

namespace operations.Application.Warehouse.StockReconciliation.Queries.GetStockRecons;

public sealed record GetStockReconsQuery(int Page, int PageSize, string? Status)
    : IQuery<PaginatedList<StockReconciliationDto>>;

public sealed class GetStockReconsQueryHandler
    : IQueryHandler<GetStockReconsQuery, PaginatedList<StockReconciliationDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetStockReconsQueryHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<StockReconciliationDto>> HandleAsync(GetStockReconsQuery query, CancellationToken cancellationToken)
    {
        var q = query;
        var brandId = _user.RequireBrandId();
        var src     = _db.StockReconciliations.Where(r => r.BrandId == brandId);
        if (!string.IsNullOrEmpty(q.Status)) src = src.Where(r => r.Status == q.Status);

        return PaginatedList<StockReconciliationDto>.CreateAsync(
            src.OrderByDescending(r => r.CreatedAt)
                .Select(r => CreateStockReconCommandHandler.ToDto(r)),
            q.Page, q.PageSize, cancellationToken);
    }
}
