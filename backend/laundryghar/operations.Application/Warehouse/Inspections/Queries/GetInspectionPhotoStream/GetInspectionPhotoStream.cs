using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Common.Storage;

namespace operations.Application.Warehouse.Inspections.Queries.GetInspectionPhotoStream;

// ── Stream a single inspection photo (by photo id) ────────────────────────────

/// <summary>Result of fetching a photo stream — carries stream + MIME type for the response.</summary>
public sealed record PhotoStreamResult(Stream Stream, string ContentType, string FileName);

public sealed record GetInspectionPhotoStreamQuery(Guid PhotoId) : IQuery<PhotoStreamResult?>;

public sealed class GetInspectionPhotoStreamQueryHandler
    : IQueryHandler<GetInspectionPhotoStreamQuery, PhotoStreamResult?>
{
    private readonly IOperationsDbContext  _db;
    private readonly ICurrentUser         _user;
    private readonly IFileStorageProvider _storage;

    public GetInspectionPhotoStreamQueryHandler(
        IOperationsDbContext db, ICurrentUser user, IFileStorageProvider storage)
    {
        _db      = db;
        _user    = user;
        _storage = storage;
    }

    public async Task<PhotoStreamResult?> HandleAsync(
        GetInspectionPhotoStreamQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();

        var photo = await _db.GarmentInspectionPhotos
            .Where(p => p.Id == query.PhotoId && p.BrandId == brandId)
            .Select(p => new { p.S3Key, p.MimeType })
            .FirstOrDefaultAsync(cancellationToken);

        if (photo is null) return null;

        var stream   = await _storage.OpenReadAsync(photo.S3Key, cancellationToken);
        var ext      = FileStorageKeyGenerator.ResolveExtension(photo.MimeType);
        var fileName = $"photo-{query.PhotoId:N}.{ext}";

        return new PhotoStreamResult(stream, photo.MimeType, fileName);
    }
}
