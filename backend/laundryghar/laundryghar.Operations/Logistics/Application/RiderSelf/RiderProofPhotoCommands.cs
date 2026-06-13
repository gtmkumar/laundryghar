using laundryghar.Logistics.Infrastructure.Auth;
using laundryghar.Logistics.Infrastructure.Services;
using FluentValidation;
using laundryghar.ServiceDefaults.Storage;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using MediatR;

namespace laundryghar.Logistics.Application.RiderSelf;

// ── Upload Proof-of-Delivery photo (rider lane) ────────────────────────────────
//
// A rider POSTs a multipart photo to prove delivery (or pickup handover).
// The endpoint gate is RiderOnly; self-filtering ensures the assignment belongs
// to this rider + brand. Stores the key in delivery_assignments.proof_photo_s3_key
// and proof_photo_taken_at, then returns the updated RiderTaskDto.
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Upload proof-of-delivery photo for a delivery assignment the calling rider owns.
/// </summary>
public sealed record UploadProofPhotoCommand(
    Guid      AssignmentId,
    Guid      UserId,
    Guid      BrandId,
    IFormFile File) : IRequest<RiderTaskResult>;

public sealed class UploadProofPhotoHandler : IRequestHandler<UploadProofPhotoCommand, RiderTaskResult>
{
    private readonly LaundryGharDbContext  _db;
    private readonly IFileStorageProvider _storage;

    public UploadProofPhotoHandler(LaundryGharDbContext db, IFileStorageProvider storage)
    {
        _db      = db;
        _storage = storage;
    }

    public async Task<RiderTaskResult> Handle(UploadProofPhotoCommand cmd, CancellationToken ct)
    {
        // Resolve rider_id from user_id + brand_id
        var rider = await _db.Riders
            .Where(r => r.UserId == cmd.UserId && r.BrandId == cmd.BrandId)
            .Select(r => new { r.Id })
            .FirstOrDefaultAsync(ct);
        if (rider is null) return RiderTaskResult.NotFound();

        // Self-filter: assignment must belong to THIS rider + brand
        var da = await _db.DeliveryAssignments
            .FirstOrDefaultAsync(x => x.Id           == cmd.AssignmentId
                                   && x.RiderId       == rider.Id
                                   && x.BrandId       == cmd.BrandId, ct);
        if (da is null) return RiderTaskResult.NotFound();

        var now = DateTimeOffset.UtcNow;

        await using var stream = cmd.File.OpenReadStream();
        var key = await _storage.SaveAsync(
            stream, cmd.File.ContentType, "proof", cmd.BrandId, ct);

        da.ProofPhotoS3Key  = key;
        da.ProofPhotoTakenAt = now;
        da.UpdatedAt        = now;
        da.UpdatedBy        = cmd.UserId;

        await _db.SaveChangesAsync(ct);

        // Return a full RiderTaskDto so the mobile client can refresh its task state
        var (o, c, addr) = await LoadOrderAsync(da, ct);
        var payoutCfg    = await Payout.PayoutConfig.LoadAsync(_db, cmd.BrandId, ct);

        return RiderTaskResult.Ok(RiderTaskMapper.ToDto(da, o, c, addr, payoutCfg));
    }

    private async Task<(Order?, Customer?, CustomerAddress?)> LoadOrderAsync(
        DeliveryAssignment da, CancellationToken ct)
    {
        if (da.OrderId is null || da.OrderCreatedAt is null) return (null, null, null);

        var o = await _db.Orders.FirstOrDefaultAsync(
            x => x.Id == da.OrderId && x.CreatedAt == da.OrderCreatedAt, ct);
        if (o is null) return (null, null, null);

        var c     = await _db.Customers.FirstOrDefaultAsync(x => x.Id == o.CustomerId, ct);
        var addrId = da.LegType == "pickup" ? o.PickupAddressId : o.DeliveryAddressId;
        var addr  = addrId.HasValue
            ? await _db.CustomerAddresses.FirstOrDefaultAsync(a => a.Id == addrId.Value, ct)
            : null;

        return (o, c, addr);
    }
}

public sealed class UploadProofPhotoValidator : AbstractValidator<UploadProofPhotoCommand>
{
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };
    private const long MaxBytes = 10 * 1024 * 1024; // 10 MB

    public UploadProofPhotoValidator()
    {
        RuleFor(x => x.AssignmentId).NotEmpty();

        RuleFor(x => x.File)
            .NotNull().WithMessage("A proof photo is required.")
            .Must(f => AllowedMimeTypes.Contains(f.ContentType))
            .WithMessage("Photo must be image/jpeg, image/png, or image/webp.")
            .Must(f => f.Length <= MaxBytes)
            .WithMessage("Photo must be ≤ 10 MB.");
    }
}
