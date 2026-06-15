using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Batches.Commands.CreateWarehouseBatch;
using operations.Application.Warehouse.Batches.Dtos;

namespace operations.Application.Warehouse.Batches.Queries.GetBatches;

public sealed record GetBatchesQuery(int Page, int PageSize, string? Status, Guid? WarehouseId)
    : IQuery<PaginatedList<WarehouseBatchDto>>;

public sealed class GetBatchesQueryHandler : IQueryHandler<GetBatchesQuery, PaginatedList<WarehouseBatchDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetBatchesQueryHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<WarehouseBatchDto>> HandleAsync(GetBatchesQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var q       = _db.WarehouseBatches.Where(b => b.BrandId == brandId);

        if (!string.IsNullOrEmpty(query.Status)) q = q.Where(b => b.Status == query.Status);
        if (query.WarehouseId.HasValue) q = q.Where(b => b.WarehouseId == query.WarehouseId.Value);

        return PaginatedList<WarehouseBatchDto>.CreateAsync(
            q.OrderByDescending(b => b.CreatedAt)
                .Select(b => CreateWarehouseBatchCommandHandler.ToDto(b)),
            query.Page, query.PageSize, cancellationToken);
    }
}
