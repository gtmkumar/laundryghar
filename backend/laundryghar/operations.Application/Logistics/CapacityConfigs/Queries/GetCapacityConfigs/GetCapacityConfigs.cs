using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.CapacityConfigs.Commands.CreateCapacityConfig;
using operations.Application.Logistics.CapacityConfigs.Dtos;

namespace operations.Application.Logistics.CapacityConfigs.Queries.GetCapacityConfigs;

// ── List CapacityConfigs ──────────────────────────────────────────────────────

public sealed record GetCapacityConfigsQuery(int Page, int PageSize, Guid? RiderId, string? Status)
    : IQuery<PaginatedList<RiderCapacityConfigDto>>;

public sealed class GetCapacityConfigsHandler
    : IQueryHandler<GetCapacityConfigsQuery, PaginatedList<RiderCapacityConfigDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetCapacityConfigsHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<RiderCapacityConfigDto>> HandleAsync(GetCapacityConfigsQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var q       = _db.RiderCapacityConfigs.Where(c => c.BrandId == brandId);

        if (query.RiderId.HasValue)
            q = q.Where(c => c.RiderId == query.RiderId.Value);
        if (!string.IsNullOrEmpty(query.Status))
            q = q.Where(c => c.Status == query.Status);

        return PaginatedList<RiderCapacityConfigDto>.CreateAsync(
            q.OrderByDescending(c => c.CreatedAt)
             .Select(c => CreateCapacityConfigHandler.ToDto(c)),
            query.Page, query.PageSize, cancellationToken);
    }
}
