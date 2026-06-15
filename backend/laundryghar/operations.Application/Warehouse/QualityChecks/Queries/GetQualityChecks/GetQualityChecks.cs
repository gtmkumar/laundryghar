using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.QualityChecks.Commands.CreateQualityCheck;
using operations.Application.Warehouse.QualityChecks.Dtos;

namespace operations.Application.Warehouse.QualityChecks.Queries.GetQualityChecks;

public sealed record GetQualityChecksQuery(Guid? GarmentId, Guid? BatchId, int Page, int PageSize)
    : IQuery<PaginatedList<QualityCheckDto>>;

public sealed class GetQualityChecksQueryHandler : IQueryHandler<GetQualityChecksQuery, PaginatedList<QualityCheckDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetQualityChecksQueryHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<QualityCheckDto>> HandleAsync(GetQualityChecksQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var q       = _db.QualityChecks.Where(qc => qc.BrandId == brandId);

        if (query.GarmentId.HasValue) q = q.Where(qc => qc.GarmentId == query.GarmentId.Value);
        if (query.BatchId.HasValue)   q = q.Where(qc => qc.BatchId   == query.BatchId.Value);

        return PaginatedList<QualityCheckDto>.CreateAsync(
            q.OrderByDescending(qc => qc.InspectedAt)
                .Select(qc => CreateQualityCheckCommandHandler.ToDto(qc)),
            query.Page, query.PageSize, cancellationToken);
    }
}
