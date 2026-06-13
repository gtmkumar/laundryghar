using laundryghar.Logistics.Application.RiderSelf;
using laundryghar.ServiceDefaults.Storage;
using laundryghar.SharedDataModel.Enums;
using MediatR;

namespace laundryghar.Logistics.Application.Riders.Commands;

// ── Admin: review a rider's KYC documents + vehicle ─────────────────────────────

/// <summary>Admin view of a rider's verification: KYC status, vehicle gate, and documents.</summary>
public sealed record GetRiderVerificationQuery(Guid RiderId) : IRequest<RiderVerificationView?>;

public sealed class GetRiderVerificationHandler : IRequestHandler<GetRiderVerificationQuery, RiderVerificationView?>
{
    private readonly LaundryGharDbContext _db;
    public GetRiderVerificationHandler(LaundryGharDbContext db) => _db = db;

    public async Task<RiderVerificationView?> Handle(GetRiderVerificationQuery q, CancellationToken ct)
    {
        var rider = await _db.Riders.AsNoTracking().FirstOrDefaultAsync(r => r.Id == q.RiderId, ct);
        if (rider is null) return null;

        var docs = await _db.RiderDocuments.AsNoTracking()
            .Where(d => d.RiderId == rider.Id).OrderBy(d => d.DocType).ToListAsync(ct);

        return new RiderVerificationView(
            rider.KycStatus, rider.VehicleVerificationStatus, rider.VehicleRejectionReason,
            docs.Select(UploadRiderDocumentHandler.ToDto).ToList());
    }
}

/// <summary>Streams a rider document's binary by document id (key never exposed).</summary>
public sealed record GetRiderDocumentStreamQuery(Guid DocumentId) : IRequest<RiderDocumentStream?>;
public sealed record RiderDocumentStream(Stream Stream, string ContentType, string FileName);

public sealed class GetRiderDocumentStreamHandler : IRequestHandler<GetRiderDocumentStreamQuery, RiderDocumentStream?>
{
    private readonly LaundryGharDbContext _db;
    private readonly IFileStorageProvider _storage;
    public GetRiderDocumentStreamHandler(LaundryGharDbContext db, IFileStorageProvider storage)
    { _db = db; _storage = storage; }

    public async Task<RiderDocumentStream?> Handle(GetRiderDocumentStreamQuery q, CancellationToken ct)
    {
        var doc = await _db.RiderDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == q.DocumentId, ct);
        if (doc is null) return null;
        var stream = await _storage.OpenReadAsync(doc.StorageKey, ct);
        return new RiderDocumentStream(stream, doc.MimeType, doc.FileName);
    }
}

/// <summary>Admin approves or rejects a single rider document.</summary>
public sealed record ReviewRiderDocumentCommand(Guid DocumentId, bool Approve, string? Reason, Guid? ActorId)
    : IRequest<RiderDocumentDto?>;

public sealed class ReviewRiderDocumentHandler : IRequestHandler<ReviewRiderDocumentCommand, RiderDocumentDto?>
{
    private readonly LaundryGharDbContext _db;
    public ReviewRiderDocumentHandler(LaundryGharDbContext db) => _db = db;

    public async Task<RiderDocumentDto?> Handle(ReviewRiderDocumentCommand cmd, CancellationToken ct)
    {
        var doc = await _db.RiderDocuments.FirstOrDefaultAsync(d => d.Id == cmd.DocumentId, ct);
        if (doc is null) return null;

        var now = DateTimeOffset.UtcNow;
        doc.Status = cmd.Approve ? RiderDocumentStatus.Approved : RiderDocumentStatus.Rejected;
        doc.RejectionReason = cmd.Approve ? null : cmd.Reason;
        doc.ReviewedBy = cmd.ActorId;
        doc.ReviewedAt = now;
        doc.UpdatedAt = now;
        doc.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return UploadRiderDocumentHandler.ToDto(doc);
    }
}

/// <summary>
/// Admin approves or rejects a rider's VEHICLE. Approval (combined with KYC verified) is
/// what lets the rider receive dispatch offers/assignments. Approving also stamps the
/// reviewer; rejecting records a reason and blocks dispatch.
/// </summary>
public sealed record ReviewRiderVehicleCommand(Guid RiderId, bool Approve, string? Reason, Guid? ActorId)
    : IRequest<RiderVerificationView?>;

public sealed class ReviewRiderVehicleHandler : IRequestHandler<ReviewRiderVehicleCommand, RiderVerificationView?>
{
    private readonly LaundryGharDbContext _db;
    public ReviewRiderVehicleHandler(LaundryGharDbContext db) => _db = db;

    public async Task<RiderVerificationView?> Handle(ReviewRiderVehicleCommand cmd, CancellationToken ct)
    {
        var rider = await _db.Riders.FirstOrDefaultAsync(r => r.Id == cmd.RiderId, ct);
        if (rider is null) return null;

        var now = DateTimeOffset.UtcNow;
        rider.VehicleVerificationStatus = cmd.Approve
            ? VehicleVerificationStatus.Approved
            : VehicleVerificationStatus.Rejected;
        rider.VehicleRejectionReason = cmd.Approve ? null : cmd.Reason;
        rider.VehicleVerifiedAt = cmd.Approve ? now : null;
        rider.VehicleVerifiedBy = cmd.Approve ? cmd.ActorId : null;
        rider.UpdatedAt = now;
        rider.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);

        var docs = await _db.RiderDocuments.AsNoTracking()
            .Where(d => d.RiderId == rider.Id).OrderBy(d => d.DocType).ToListAsync(ct);
        return new RiderVerificationView(
            rider.KycStatus, rider.VehicleVerificationStatus, rider.VehicleRejectionReason,
            docs.Select(UploadRiderDocumentHandler.ToDto).ToList());
    }
}
