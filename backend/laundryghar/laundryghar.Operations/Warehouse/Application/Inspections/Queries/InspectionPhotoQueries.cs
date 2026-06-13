using laundryghar.ServiceDefaults.Storage;
using laundryghar.Warehouse.Application.Inspections.Dtos;
using MediatR;

namespace laundryghar.Warehouse.Application.Inspections.Queries;

// ── List photos for an inspection ─────────────────────────────────────────────

public sealed record GetInspectionPhotosQuery(Guid InspectionId) : IRequest<List<InspectionPhotoDto>>;

public sealed class GetInspectionPhotosHandler
    : IRequestHandler<GetInspectionPhotosQuery, List<InspectionPhotoDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser        _user;

    public GetInspectionPhotosHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<List<InspectionPhotoDto>> Handle(
        GetInspectionPhotosQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var photos = await _db.GarmentInspectionPhotos
            .Where(p => p.InspectionId == q.InspectionId && p.BrandId == brandId)
            .OrderByDescending(p => p.IsPrimary)
            .ThenBy(p => p.CreatedAt)
            .Select(p => new InspectionPhotoDto(
                p.Id, p.S3Key, p.View, p.MimeType, p.IsPrimary, p.CreatedAt))
            .ToListAsync(ct);

        return photos;
    }
}

// ── Stream a single inspection photo (by photo id) ────────────────────────────

/// <summary>Result of fetching a photo stream — carries stream + MIME type for the response.</summary>
public sealed record PhotoStreamResult(Stream Stream, string ContentType, string FileName);

public sealed record GetInspectionPhotoStreamQuery(Guid PhotoId) : IRequest<PhotoStreamResult?>;

public sealed class GetInspectionPhotoStreamHandler
    : IRequestHandler<GetInspectionPhotoStreamQuery, PhotoStreamResult?>
{
    private readonly LaundryGharDbContext  _db;
    private readonly ICurrentUser         _user;
    private readonly IFileStorageProvider _storage;

    public GetInspectionPhotoStreamHandler(
        LaundryGharDbContext db, ICurrentUser user, IFileStorageProvider storage)
    {
        _db      = db;
        _user    = user;
        _storage = storage;
    }

    public async Task<PhotoStreamResult?> Handle(
        GetInspectionPhotoStreamQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var photo = await _db.GarmentInspectionPhotos
            .Where(p => p.Id == q.PhotoId && p.BrandId == brandId)
            .Select(p => new { p.S3Key, p.MimeType })
            .FirstOrDefaultAsync(ct);

        if (photo is null) return null;

        var stream   = await _storage.OpenReadAsync(photo.S3Key, ct);
        var ext      = FileStorageKeyGenerator.ResolveExtension(photo.MimeType);
        var fileName = $"photo-{q.PhotoId:N}.{ext}";

        return new PhotoStreamResult(stream, photo.MimeType, fileName);
    }
}
