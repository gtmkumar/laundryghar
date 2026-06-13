using laundryghar.Warehouse.Application.QualityChecks.Commands;
using laundryghar.Warehouse.Application.QualityChecks.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Warehouse.Application.QualityChecks.Queries;

public sealed record GetQualityChecksQuery(Guid? GarmentId, Guid? BatchId, int Page, int PageSize)
    : IRequest<PaginatedList<QualityCheckDto>>;

public sealed class GetQualityChecksHandler : IRequestHandler<GetQualityChecksQuery, PaginatedList<QualityCheckDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetQualityChecksHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<QualityCheckDto>> Handle(GetQualityChecksQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.QualityChecks.Where(qc => qc.BrandId == brandId);

        if (q.GarmentId.HasValue) query = query.Where(qc => qc.GarmentId == q.GarmentId.Value);
        if (q.BatchId.HasValue)   query = query.Where(qc => qc.BatchId   == q.BatchId.Value);

        return PaginatedList<QualityCheckDto>.CreateAsync(
            query.OrderByDescending(qc => qc.InspectedAt)
                .Select(qc => CreateQualityCheckHandler.ToDto(qc)),
            q.Page, q.PageSize, ct);
    }
}
