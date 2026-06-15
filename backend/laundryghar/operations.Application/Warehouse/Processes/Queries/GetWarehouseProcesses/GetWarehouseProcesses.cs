using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Processes.Commands.CreateWarehouseProcess;
using operations.Application.Warehouse.Processes.Dtos;

namespace operations.Application.Warehouse.Processes.Queries.GetWarehouseProcesses;

public sealed record GetWarehouseProcessesQuery(int Page, int PageSize)
    : IQuery<PaginatedList<WarehouseProcessDto>>;

public sealed class GetWarehouseProcessesQueryHandler
    : IQueryHandler<GetWarehouseProcessesQuery, PaginatedList<WarehouseProcessDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetWarehouseProcessesQueryHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<WarehouseProcessDto>> HandleAsync(GetWarehouseProcessesQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        return PaginatedList<WarehouseProcessDto>.CreateAsync(
            _db.WarehouseProcesses
                .Where(p => p.BrandId == brandId)
                .OrderBy(p => p.SequenceOrder)
                .Select(p => CreateWarehouseProcessCommandHandler.ToDto(p)),
            query.Page, query.PageSize, cancellationToken);
    }
}
