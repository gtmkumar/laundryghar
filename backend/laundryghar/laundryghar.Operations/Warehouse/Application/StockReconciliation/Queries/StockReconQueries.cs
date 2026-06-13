using laundryghar.Warehouse.Infrastructure.Auth;
using laundryghar.Warehouse.Infrastructure.Services;
using laundryghar.Warehouse.Application.StockReconciliation.Commands;
using laundryghar.Warehouse.Application.StockReconciliation.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Warehouse.Application.StockReconciliation.Queries;

public sealed record GetStockReconsQuery(int Page, int PageSize, string? Status)
    : IRequest<PaginatedList<StockReconciliationDto>>;

public sealed class GetStockReconsHandler
    : IRequestHandler<GetStockReconsQuery, PaginatedList<StockReconciliationDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetStockReconsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<StockReconciliationDto>> Handle(GetStockReconsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.StockReconciliations.Where(r => r.BrandId == brandId);
        if (!string.IsNullOrEmpty(q.Status)) query = query.Where(r => r.Status == q.Status);

        return PaginatedList<StockReconciliationDto>.CreateAsync(
            query.OrderByDescending(r => r.CreatedAt)
                .Select(r => CreateStockReconHandler.ToDto(r)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetStockReconByIdQuery(Guid Id) : IRequest<StockReconciliationDto?>;

public sealed class GetStockReconByIdHandler
    : IRequestHandler<GetStockReconByIdQuery, StockReconciliationDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetStockReconByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<StockReconciliationDto?> Handle(GetStockReconByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var r = await _db.StockReconciliations
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return r is null ? null : CreateStockReconHandler.ToDto(r);
    }
}
