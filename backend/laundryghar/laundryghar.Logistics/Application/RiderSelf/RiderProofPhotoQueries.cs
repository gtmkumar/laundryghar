using laundryghar.ServiceDefaults.Storage;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using MediatR;

namespace laundryghar.Logistics.Application.RiderSelf;

// ── Admin: stream a rider proof-of-delivery photo ────────────────────────────────
//
// Admin/dispatch can view the proof photo for any delivery assignment in their brand.
// Requires permission:rider.read. Streams the photo bytes so the raw storage key
// (which includes the brand directory) is never exposed in the API surface.
// ──────────────────────────────────────────────────────────────────────────────────

/// <summary>Carries the open stream + content-type for the HTTP response.</summary>
public sealed record ProofPhotoStreamResult(Stream Stream, string ContentType, string FileName);

/// <summary>Fetch the proof-of-delivery photo for a delivery assignment (admin lane).</summary>
public sealed record GetProofPhotoStreamQuery(Guid AssignmentId, Guid BrandId)
    : IRequest<ProofPhotoStreamResult?>;

public sealed class GetProofPhotoStreamHandler
    : IRequestHandler<GetProofPhotoStreamQuery, ProofPhotoStreamResult?>
{
    private readonly LaundryGharDbContext  _db;
    private readonly IFileStorageProvider _storage;

    public GetProofPhotoStreamHandler(LaundryGharDbContext db, IFileStorageProvider storage)
    {
        _db      = db;
        _storage = storage;
    }

    public async Task<ProofPhotoStreamResult?> Handle(
        GetProofPhotoStreamQuery q, CancellationToken ct)
    {
        var assignment = await _db.DeliveryAssignments
            .Where(da => da.Id == q.AssignmentId && da.BrandId == q.BrandId)
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
        var fileName = $"proof-{q.AssignmentId:N}.{ext}";

        return new ProofPhotoStreamResult(stream, mime, fileName);
    }
}
