using laundryghar.Warehouse.Infrastructure.Auth;
using laundryghar.Warehouse.Infrastructure.Services;
using laundryghar.Warehouse.Application.Batches.Commands;
using laundryghar.Warehouse.Application.Batches.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Warehouse.Application.Batches.Queries;

public sealed record GetBatchesQuery(int Page, int PageSize, string? Status, Guid? WarehouseId)
    : IRequest<PaginatedList<WarehouseBatchDto>>;

public sealed class GetBatchesHandler : IRequestHandler<GetBatchesQuery, PaginatedList<WarehouseBatchDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetBatchesHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<WarehouseBatchDto>> Handle(GetBatchesQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.WarehouseBatches.Where(b => b.BrandId == brandId);

        if (!string.IsNullOrEmpty(q.Status)) query = query.Where(b => b.Status == q.Status);
        if (q.WarehouseId.HasValue) query = query.Where(b => b.WarehouseId == q.WarehouseId.Value);

        return PaginatedList<WarehouseBatchDto>.CreateAsync(
            query.OrderByDescending(b => b.CreatedAt)
                .Select(b => CreateWarehouseBatchHandler.ToDto(b)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetBatchByIdQuery(Guid Id) : IRequest<WarehouseBatchDto?>;

public sealed class GetBatchByIdHandler : IRequestHandler<GetBatchByIdQuery, WarehouseBatchDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetBatchByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<WarehouseBatchDto?> Handle(GetBatchByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var b = await _db.WarehouseBatches
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return b is null ? null : CreateWarehouseBatchHandler.ToDto(b);
    }
}
