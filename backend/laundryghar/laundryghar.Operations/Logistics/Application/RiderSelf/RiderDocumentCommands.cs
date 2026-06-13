using laundryghar.Logistics.Infrastructure.Auth;
using laundryghar.Logistics.Infrastructure.Services;
using FluentValidation;
using laundryghar.ServiceDefaults.Storage;
using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.SharedDataModel.Enums;
using MediatR;

namespace laundryghar.Logistics.Application.RiderSelf;

// ── DTOs ───────────────────────────────────────────────────────────────────────

public sealed record RiderDocumentDto(
    Guid Id,
    string DocType,
    string FileName,
    string Status,
    string? RejectionReason,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset UploadedAt);

/// <summary>A rider's verification snapshot: identity (KYC) + vehicle gate + documents.</summary>
public sealed record RiderVerificationView(
    string KycStatus,
    string VehicleVerificationStatus,
    string? VehicleRejectionReason,
    IReadOnlyList<RiderDocumentDto> Documents);

// ── Rider-self: upload a KYC document ───────────────────────────────────────────

/// <summary>
/// Rider uploads (or replaces) a KYC document. Uploading moves a 'pending' rider into
/// review: kyc_status → submitted, vehicle_verification_status → under_review. A new
/// upload of the same doc type replaces the prior file and resets its review state.
/// </summary>
public sealed record UploadRiderDocumentCommand(
    Guid UserId, Guid BrandId, string DocType, IFormFile File) : IRequest<RiderDocumentDto>;

public sealed class UploadRiderDocumentHandler : IRequestHandler<UploadRiderDocumentCommand, RiderDocumentDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly IFileStorageProvider _storage;

    public UploadRiderDocumentHandler(LaundryGharDbContext db, IFileStorageProvider storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<RiderDocumentDto> Handle(UploadRiderDocumentCommand cmd, CancellationToken ct)
    {
        var rider = await _db.Riders
            .FirstOrDefaultAsync(r => r.UserId == cmd.UserId && r.BrandId == cmd.BrandId, ct)
            ?? throw new KeyNotFoundException("Rider profile not found.");

        var now = DateTimeOffset.UtcNow;

        await using var stream = cmd.File.OpenReadStream();
        var key = await _storage.SaveAsync(stream, cmd.File.ContentType, "rider/documents", cmd.BrandId, ct);

        // Replace any existing document of the same type (one current file per type).
        var existing = await _db.RiderDocuments
            .FirstOrDefaultAsync(d => d.RiderId == rider.Id && d.DocType == cmd.DocType, ct);

        RiderDocument doc;
        string? oldKey = null;
        if (existing is not null)
        {
            oldKey = existing.StorageKey;
            existing.StorageKey = key;
            existing.FileName = cmd.File.FileName;
            existing.MimeType = cmd.File.ContentType;
            existing.Bytes = cmd.File.Length;
            existing.Status = RiderDocumentStatus.Pending;
            existing.RejectionReason = null;
            existing.ReviewedBy = null;
            existing.ReviewedAt = null;
            existing.UpdatedAt = now;
            existing.UpdatedBy = cmd.UserId;
            doc = existing;
        }
        else
        {
            doc = new RiderDocument
            {
                Id = Guid.NewGuid(),
                RiderId = rider.Id,
                BrandId = cmd.BrandId,
                DocType = cmd.DocType,
                StorageKey = key,
                FileName = cmd.File.FileName,
                MimeType = cmd.File.ContentType,
                Bytes = cmd.File.Length,
                Status = RiderDocumentStatus.Pending,
                Metadata = "{}",
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = cmd.UserId,
                UpdatedBy = cmd.UserId
            };
            _db.RiderDocuments.Add(doc);
        }

        // Move the rider into review when they were still at the initial pending state.
        if (rider.KycStatus == "pending") { rider.KycStatus = "submitted"; rider.UpdatedAt = now; }
        if (rider.VehicleVerificationStatus == VehicleVerificationStatus.Pending)
        { rider.VehicleVerificationStatus = VehicleVerificationStatus.UnderReview; rider.UpdatedAt = now; }

        await _db.SaveChangesAsync(ct);

        if (!string.IsNullOrEmpty(oldKey))
            await _storage.DeleteAsync(oldKey, ct);

        return ToDto(doc);
    }

    internal static RiderDocumentDto ToDto(RiderDocument d) =>
        new(d.Id, d.DocType, d.FileName, d.Status, d.RejectionReason, d.ReviewedAt, d.CreatedAt);
}

public sealed class UploadRiderDocumentValidator : AbstractValidator<UploadRiderDocumentCommand>
{
    private static readonly HashSet<string> AllowedMime = new(StringComparer.OrdinalIgnoreCase)
        { "image/jpeg", "image/png", "image/webp", "application/pdf" };
    private const long MaxBytes = 5 * 1024 * 1024;

    public UploadRiderDocumentValidator()
    {
        RuleFor(x => x.DocType).Must(RiderDocumentType.IsValid)
            .WithMessage($"DocType must be one of: {string.Join(", ", RiderDocumentType.All)}.");
        RuleFor(x => x.File).NotNull();
        RuleFor(x => x.File.Length).GreaterThan(0).LessThanOrEqualTo(MaxBytes)
            .When(x => x.File is not null).WithMessage("File must be 1 byte–5 MB.");
        RuleFor(x => x.File.ContentType).Must(ct => AllowedMime.Contains(ct))
            .When(x => x.File is not null)
            .WithMessage("File must be JPEG, PNG, WebP, or PDF.");
    }
}

// ── Rider-self: my verification status + documents ──────────────────────────────

public sealed record GetMyRiderVerificationQuery(Guid UserId, Guid BrandId) : IRequest<RiderVerificationView?>;

public sealed class GetMyRiderVerificationHandler : IRequestHandler<GetMyRiderVerificationQuery, RiderVerificationView?>
{
    private readonly LaundryGharDbContext _db;
    public GetMyRiderVerificationHandler(LaundryGharDbContext db) => _db = db;

    public async Task<RiderVerificationView?> Handle(GetMyRiderVerificationQuery q, CancellationToken ct)
    {
        var rider = await _db.Riders.AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == q.UserId && r.BrandId == q.BrandId, ct);
        if (rider is null) return null;

        var docs = await _db.RiderDocuments.AsNoTracking()
            .Where(d => d.RiderId == rider.Id)
            .OrderBy(d => d.DocType)
            .ToListAsync(ct);

        return new RiderVerificationView(
            rider.KycStatus,
            rider.VehicleVerificationStatus,
            rider.VehicleRejectionReason,
            docs.Select(UploadRiderDocumentHandler.ToDto).ToList());
    }
}
