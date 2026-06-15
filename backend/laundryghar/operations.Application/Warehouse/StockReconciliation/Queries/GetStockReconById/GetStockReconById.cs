using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.StockReconciliation.Commands.CreateStockRecon;
using operations.Application.Warehouse.StockReconciliation.Dtos;

namespace operations.Application.Warehouse.StockReconciliation.Queries.GetStockReconById;

public sealed record GetStockReconByIdQuery(Guid Id) : IQuery<StockReconciliationDto?>;

public sealed class GetStockReconByIdQueryHandler
    : IQueryHandler<GetStockReconByIdQuery, StockReconciliationDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetStockReconByIdQueryHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<StockReconciliationDto?> HandleAsync(GetStockReconByIdQuery query, CancellationToken cancellationToken)
    {
        var q = query;
        var brandId = _user.RequireBrandId();
        var r = await _db.StockReconciliations
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, cancellationToken);
        return r is null ? null : CreateStockReconCommandHandler.ToDto(r);
    }
}
