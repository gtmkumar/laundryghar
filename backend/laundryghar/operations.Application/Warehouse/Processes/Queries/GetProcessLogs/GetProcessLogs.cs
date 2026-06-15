using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Processes.Dtos;

namespace operations.Application.Warehouse.Processes.Queries.GetProcessLogs;

public sealed record GetProcessLogsQuery(Guid? GarmentId, Guid? BatchId, int Page, int PageSize)
    : IQuery<PaginatedList<ProcessLogEntryDto>>;

public sealed class GetProcessLogsQueryHandler : IQueryHandler<GetProcessLogsQuery, PaginatedList<ProcessLogEntryDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetProcessLogsQueryHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<ProcessLogEntryDto>> HandleAsync(GetProcessLogsQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var q       = _db.ProcessLogs.Where(pl => pl.BrandId == brandId);

        if (query.GarmentId.HasValue) q = q.Where(pl => pl.GarmentId == query.GarmentId.Value);
        if (query.BatchId.HasValue)   q = q.Where(pl => pl.BatchId   == query.BatchId.Value);

        return PaginatedList<ProcessLogEntryDto>.CreateAsync(
            q.OrderByDescending(pl => pl.OccurredAt)
                .Select(pl => new ProcessLogEntryDto(
                    pl.Id, pl.BrandId, pl.WarehouseId, pl.GarmentId,
                    pl.TagCode, pl.ProcessCode, pl.Action,
                    pl.FromStage, pl.ToStage, pl.OccurredAt, pl.CreatedAt)),
            query.Page, query.PageSize, cancellationToken);
    }
}
