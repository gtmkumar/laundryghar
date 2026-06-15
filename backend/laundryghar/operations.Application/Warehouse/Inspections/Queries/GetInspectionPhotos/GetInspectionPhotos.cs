using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Inspections.Dtos;

namespace operations.Application.Warehouse.Inspections.Queries.GetInspectionPhotos;

// ── List photos for an inspection ─────────────────────────────────────────────

public sealed record GetInspectionPhotosQuery(Guid InspectionId) : IQuery<List<InspectionPhotoDto>>;

public sealed class GetInspectionPhotosQueryHandler
    : IQueryHandler<GetInspectionPhotosQuery, List<InspectionPhotoDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser        _user;

    public GetInspectionPhotosQueryHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<List<InspectionPhotoDto>> HandleAsync(
        GetInspectionPhotosQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();

        var photos = await _db.GarmentInspectionPhotos
            .Where(p => p.InspectionId == query.InspectionId && p.BrandId == brandId)
            .OrderByDescending(p => p.IsPrimary)
            .ThenBy(p => p.CreatedAt)
            .Select(p => new InspectionPhotoDto(
                p.Id, p.S3Key, p.View, p.MimeType, p.IsPrimary, p.CreatedAt))
            .ToListAsync(cancellationToken);

        return photos;
    }
}
