using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Batches.Commands.CreateWarehouseBatch;
using operations.Application.Warehouse.Batches.Dtos;

namespace operations.Application.Warehouse.Batches.Queries.GetBatchById;

public sealed record GetBatchByIdQuery(Guid Id) : IQuery<WarehouseBatchDto?>;

public sealed class GetBatchByIdQueryHandler : IQueryHandler<GetBatchByIdQuery, WarehouseBatchDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetBatchByIdQueryHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<WarehouseBatchDto?> HandleAsync(GetBatchByIdQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var b = await _db.WarehouseBatches
            .FirstOrDefaultAsync(x => x.Id == query.Id && x.BrandId == brandId, cancellationToken);
        return b is null ? null : CreateWarehouseBatchCommandHandler.ToDto(b);
    }
}
