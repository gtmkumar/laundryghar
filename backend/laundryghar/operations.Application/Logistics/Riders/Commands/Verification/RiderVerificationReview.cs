using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.RiderSelf.Commands.UploadRiderDocument;
using operations.Application.Logistics.RiderSelf.Dtos;

namespace operations.Application.Logistics.Riders.Commands.Verification;

// ── Admin: review a rider's KYC documents + vehicle ─────────────────────────────

/// <summary>Admin view of a rider's verification: KYC status, vehicle gate, and documents.</summary>
public sealed record GetRiderVerificationQuery(Guid RiderId) : IQuery<RiderVerificationView?>;

public sealed class GetRiderVerificationHandler : IQueryHandler<GetRiderVerificationQuery, RiderVerificationView?>
{
    private readonly IOperationsDbContext _db;
    public GetRiderVerificationHandler(IOperationsDbContext db) => _db = db;

    public async Task<RiderVerificationView?> HandleAsync(GetRiderVerificationQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var rider = await _db.Riders.AsNoTracking().FirstOrDefaultAsync(r => r.Id == query.RiderId, ct);
        if (rider is null) return null;

        var docs = await _db.RiderDocuments.AsNoTracking()
            .Where(d => d.RiderId == rider.Id).OrderBy(d => d.DocType).ToListAsync(ct);

        return new RiderVerificationView(
            rider.KycStatus, rider.VehicleVerificationStatus, rider.VehicleRejectionReason,
            docs.Select(UploadRiderDocumentHandler.ToDto).ToList());
    }
}

/// <summary>Streams a rider document's binary by document id (key never exposed).</summary>
public sealed record GetRiderDocumentStreamQuery(Guid DocumentId) : IQuery<RiderDocumentStream?>;
public sealed record RiderDocumentStream(Stream Stream, string ContentType, string FileName);

public sealed class GetRiderDocumentStreamHandler : IQueryHandler<GetRiderDocumentStreamQuery, RiderDocumentStream?>
{
    private readonly IOperationsDbContext _db;
    private readonly IFileStorageProvider _storage;
    public GetRiderDocumentStreamHandler(IOperationsDbContext db, IFileStorageProvider storage)
    { _db = db; _storage = storage; }

    public async Task<RiderDocumentStream?> HandleAsync(GetRiderDocumentStreamQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var doc = await _db.RiderDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == query.DocumentId, ct);
        if (doc is null) return null;
        var stream = await _storage.OpenReadAsync(doc.StorageKey, ct);
        return new RiderDocumentStream(stream, doc.MimeType, doc.FileName);
    }
}

/// <summary>Admin approves or rejects a single rider document.</summary>
public sealed record ReviewRiderDocumentCommand(Guid DocumentId, bool Approve, string? Reason, Guid? ActorId)
    : ICommand<RiderDocumentDto?>;

public sealed class ReviewRiderDocumentHandler : ICommandHandler<ReviewRiderDocumentCommand, RiderDocumentDto?>
{
    private readonly IOperationsDbContext _db;
    public ReviewRiderDocumentHandler(IOperationsDbContext db) => _db = db;

    public async Task<RiderDocumentDto?> HandleAsync(ReviewRiderDocumentCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var doc = await _db.RiderDocuments.FirstOrDefaultAsync(d => d.Id == command.DocumentId, ct);
        if (doc is null) return null;

        var now = DateTimeOffset.UtcNow;
        doc.Status = command.Approve ? RiderDocumentStatus.Approved : RiderDocumentStatus.Rejected;
        doc.RejectionReason = command.Approve ? null : command.Reason;
        doc.ReviewedBy = command.ActorId;
        doc.ReviewedAt = now;
        doc.UpdatedAt = now;
        doc.UpdatedBy = command.ActorId;
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
    : ICommand<RiderVerificationView?>;

public sealed class ReviewRiderVehicleHandler : ICommandHandler<ReviewRiderVehicleCommand, RiderVerificationView?>
{
    private readonly IOperationsDbContext _db;
    public ReviewRiderVehicleHandler(IOperationsDbContext db) => _db = db;

    public async Task<RiderVerificationView?> HandleAsync(ReviewRiderVehicleCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var rider = await _db.Riders.FirstOrDefaultAsync(r => r.Id == command.RiderId, ct);
        if (rider is null) return null;

        var now = DateTimeOffset.UtcNow;
        rider.VehicleVerificationStatus = command.Approve
            ? VehicleVerificationStatus.Approved
            : VehicleVerificationStatus.Rejected;
        rider.VehicleRejectionReason = command.Approve ? null : command.Reason;
        rider.VehicleVerifiedAt = command.Approve ? now : null;
        rider.VehicleVerifiedBy = command.Approve ? command.ActorId : null;
        rider.UpdatedAt = now;
        rider.UpdatedBy = command.ActorId;
        await _db.SaveChangesAsync(ct);

        var docs = await _db.RiderDocuments.AsNoTracking()
            .Where(d => d.RiderId == rider.Id).OrderBy(d => d.DocType).ToListAsync(ct);
        return new RiderVerificationView(
            rider.KycStatus, rider.VehicleVerificationStatus, rider.VehicleRejectionReason,
            docs.Select(UploadRiderDocumentHandler.ToDto).ToList());
    }
}
