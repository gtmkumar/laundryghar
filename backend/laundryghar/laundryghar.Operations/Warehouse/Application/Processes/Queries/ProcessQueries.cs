using laundryghar.Warehouse.Application.Processes.Commands;
using laundryghar.Warehouse.Application.Processes.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Warehouse.Application.Processes.Queries;

public sealed record GetWarehouseProcessesQuery(int Page, int PageSize)
    : IRequest<PaginatedList<WarehouseProcessDto>>;

public sealed class GetWarehouseProcessesHandler
    : IRequestHandler<GetWarehouseProcessesQuery, PaginatedList<WarehouseProcessDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetWarehouseProcessesHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<WarehouseProcessDto>> Handle(GetWarehouseProcessesQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        return PaginatedList<WarehouseProcessDto>.CreateAsync(
            _db.WarehouseProcesses
                .Where(p => p.BrandId == brandId)
                .OrderBy(p => p.SequenceOrder)
                .Select(p => CreateWarehouseProcessHandler.ToDto(p)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetProcessLogsQuery(Guid? GarmentId, Guid? BatchId, int Page, int PageSize)
    : IRequest<PaginatedList<ProcessLogEntryDto>>;

public sealed class GetProcessLogsHandler : IRequestHandler<GetProcessLogsQuery, PaginatedList<ProcessLogEntryDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetProcessLogsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<ProcessLogEntryDto>> Handle(GetProcessLogsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.ProcessLogs.Where(pl => pl.BrandId == brandId);

        if (q.GarmentId.HasValue) query = query.Where(pl => pl.GarmentId == q.GarmentId.Value);
        if (q.BatchId.HasValue)   query = query.Where(pl => pl.BatchId   == q.BatchId.Value);

        return PaginatedList<ProcessLogEntryDto>.CreateAsync(
            query.OrderByDescending(pl => pl.OccurredAt)
                .Select(pl => new ProcessLogEntryDto(
                    pl.Id, pl.BrandId, pl.WarehouseId, pl.GarmentId,
                    pl.TagCode, pl.ProcessCode, pl.Action,
                    pl.FromStage, pl.ToStage, pl.OccurredAt, pl.CreatedAt)),
            q.Page, q.PageSize, ct);
    }
}
