using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Common.Storage;

namespace operations.Application.Logistics.RiderSelf.Queries.GetProofPhotoStream;

// ── Admin: stream a rider proof-of-delivery photo ────────────────────────────────

/// <summary>Carries the open stream + content-type for the HTTP response.</summary>
public sealed record ProofPhotoStreamResult(Stream Stream, string ContentType, string FileName);

/// <summary>Fetch the proof-of-delivery photo for a delivery assignment (admin lane).</summary>
public sealed record GetProofPhotoStreamQuery(Guid AssignmentId, Guid BrandId)
    : IQuery<ProofPhotoStreamResult?>;

public sealed class GetProofPhotoStreamHandler
    : IQueryHandler<GetProofPhotoStreamQuery, ProofPhotoStreamResult?>
{
    private readonly IOperationsDbContext  _db;
    private readonly IFileStorageProvider _storage;

    public GetProofPhotoStreamHandler(IOperationsDbContext db, IFileStorageProvider storage)
    {
        _db      = db;
        _storage = storage;
    }

    public async Task<ProofPhotoStreamResult?> HandleAsync(GetProofPhotoStreamQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var assignment = await _db.DeliveryAssignments
            .Where(da => da.Id == query.AssignmentId && da.BrandId == query.BrandId)
            .Select(da => new { da.ProofPhotoS3Key })
            .FirstOrDefaultAsync(ct);

        if (assignment?.ProofPhotoS3Key is null) return null;

        var stream = await _storage.OpenReadAsync(assignment.ProofPhotoS3Key, ct);
        var ext    = FileStorageKeyGenerator.ResolveExtension(
            // Derive MIME from extension in the key itself (last segment)
            assignment.ProofPhotoS3Key.EndsWith(".png")  ? "image/png"  :
            assignment.ProofPhotoS3Key.EndsWith(".webp") ? "image/webp" : "image/jpeg");
        var mime     = assignment.ProofPhotoS3Key.EndsWith(".png")  ? "image/png"
                     : assignment.ProofPhotoS3Key.EndsWith(".webp") ? "image/webp"
                     : "image/jpeg";
        var fileName = $"proof-{query.AssignmentId:N}.{ext}";

        return new ProofPhotoStreamResult(stream, mime, fileName);
    }
}
