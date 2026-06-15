using System.Text.Json;
using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.RiderSelf.Dtos;

namespace operations.Application.Logistics.RiderSelf.Commands.SubmitPickupInspection;

// ── Rider pickup garment inspection ───────────────────────────────────────────
//
// The rider photographs the customer's garments at pickup to provide condition
// evidence before the laundry takes possession. Stored on the delivery_assignment
// metadata jsonb (garment rows don't exist until warehouse intake). Additive: a
// second call replaces the inspection block.
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Submit pickup garment inspection evidence (front photo required, back optional).
/// The <c>InspectionId</c> in the response is a stable deterministic Guid derived
/// from the assignment id so retries return the same id without a second DB read.
/// </summary>
public sealed record SubmitPickupInspectionCommand(
    Guid                AssignmentId,
    Guid                UserId,
    Guid                BrandId,
    IFormFile           FrontPhoto,
    IFormFile?          BackPhoto,
    InspectionConditions Conditions,
    string?             Notes) : ICommand<RiderTaskResult>;

public sealed class SubmitPickupInspectionHandler
    : ICommandHandler<SubmitPickupInspectionCommand, RiderTaskResult>
{
    private readonly IOperationsDbContext  _db;
    private readonly IFileStorageProvider _storage;

    public SubmitPickupInspectionHandler(IOperationsDbContext db, IFileStorageProvider storage)
    {
        _db      = db;
        _storage = storage;
    }

    public async Task<RiderTaskResult> HandleAsync(SubmitPickupInspectionCommand command, CancellationToken cancellationToken)
    {
        var ct  = cancellationToken;
        var cmd = command;

        // Resolve rider from caller's userId + brandId
        var rider = await _db.Riders
            .Where(r => r.UserId == cmd.UserId && r.BrandId == cmd.BrandId)
            .Select(r => new { r.Id })
            .FirstOrDefaultAsync(ct);
        if (rider is null) return RiderTaskResult.NotFound();

        // Self-filter: assignment must belong to THIS rider + brand AND be a pickup leg
        var da = await _db.DeliveryAssignments
            .FirstOrDefaultAsync(x => x.Id      == cmd.AssignmentId
                                   && x.RiderId  == rider.Id
                                   && x.BrandId  == cmd.BrandId
                                   && x.LegType  == "pickup", ct);
        if (da is null) return RiderTaskResult.NotFound();

        var now = DateTimeOffset.UtcNow;

        // ── Upload front photo ─────────────────────────────────────────────────
        string frontKey;
        await using (var s = cmd.FrontPhoto.OpenReadStream())
        {
            // ValidateImageBytes rejects non-image content regardless of ContentType.
            ValidateImageBytes(s);
            s.Position = 0;
            frontKey = await _storage.SaveAsync(
                s, cmd.FrontPhoto.ContentType, "inspect-front", cmd.BrandId, ct);
        }

        // ── Upload back photo (optional) ───────────────────────────────────────
        string? backKey = null;
        if (cmd.BackPhoto is not null)
        {
            await using var s = cmd.BackPhoto.OpenReadStream();
            ValidateImageBytes(s);
            s.Position = 0;
            backKey = await _storage.SaveAsync(
                s, cmd.BackPhoto.ContentType, "inspect-back", cmd.BrandId, ct);
        }

        // ── Persist evidence into delivery_assignment.metadata ─────────────────
        // We merge into the existing metadata object so other fields are preserved.
        var existingMeta = string.IsNullOrWhiteSpace(da.Metadata) || da.Metadata == "{}"
            ? new Dictionary<string, object>()
            : JsonSerializer.Deserialize<Dictionary<string, object>>(da.Metadata)
              ?? new Dictionary<string, object>();

        // Stable deterministic id: same assignment always returns the same inspectionId
        // so the mobile client can safely retry without creating phantom records.
        var inspectionId = GuidV5(cmd.AssignmentId, "pickup_inspection");

        var inspectionBlock = new
        {
            recordedAt     = now,
            frontPhotoKey  = frontKey,
            backPhotoKey   = backKey,
            conditions     = new
            {
                stains         = cmd.Conditions.Stains,
                tears          = cmd.Conditions.Tears,
                missingButtons = cmd.Conditions.MissingButtons,
            },
            notes = cmd.Notes
        };

        existingMeta["pickup_inspection"] = inspectionBlock;
        da.Metadata  = JsonSerializer.Serialize(existingMeta);
        da.Notes     ??= cmd.Notes;     // also stamp plain Notes for admin list views
        da.UpdatedAt   = now;
        da.UpdatedBy   = cmd.UserId;

        await _db.SaveChangesAsync(ct);

        var result = new RiderInspectionResult(inspectionId, cmd.AssignmentId, now);

        // Return via the task result envelope — the endpoint extracts the inner data.
        // We piggy-back on RiderTaskResult.Ok with a null task and surface the data
        // through a separate wrapper in the endpoint layer.
        return new RiderTaskResult("inspection_ok", null, JsonSerializer.Serialize(result));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sniff the first 12 bytes of the stream for known image magic bytes.
    /// Rejects files the client claims are images but aren't (SEC-3 lesson).
    /// Throws <see cref="BusinessRuleException"/> on failure.
    /// </summary>
    private static void ValidateImageBytes(Stream s)
    {
        Span<byte> header = stackalloc byte[12];
        var read = s.Read(header);
        if (read < 3)
            throw new BusinessRuleException("File is too small to be a valid image.");

        // JPEG: FF D8 FF
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return;
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (read >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return;
        // WebP: RIFF????WEBP  (bytes 0-3 = 52 49 46 46, bytes 8-11 = 57 45 42 50)
        if (read >= 12
            && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
            && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
            return;

        throw new BusinessRuleException(
            "Only JPEG, PNG, and WebP images are accepted. The uploaded file is not a supported image format.");
    }

    /// <summary>
    /// Deterministic UUID v5-style (SHA-1 namespace derivation) using the assignment
    /// id + a fixed suffix, so the same assignment always yields the same inspection id.
    /// </summary>
    private static Guid GuidV5(Guid namespaceId, string name)
    {
        var nsBytes   = namespaceId.ToByteArray();
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        using var sha = System.Security.Cryptography.SHA1.Create();
        var input     = new byte[nsBytes.Length + nameBytes.Length];
        nsBytes.CopyTo(input, 0);
        nameBytes.CopyTo(input, nsBytes.Length);
        var hash = sha.ComputeHash(input);
        // Stamp version 5 (0101) and variant bits
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash[..16]);
    }
}

public sealed class SubmitPickupInspectionValidator
    : AbstractValidator<SubmitPickupInspectionCommand>
{
    private static readonly HashSet<string> AllowedMimeTypes =
        new(StringComparer.OrdinalIgnoreCase)
        { "image/jpeg", "image/png", "image/webp" };

    private const long MaxBytes = 10 * 1024 * 1024; // 10 MB per photo

    public SubmitPickupInspectionValidator()
    {
        RuleFor(x => x.AssignmentId).NotEmpty();

        RuleFor(x => x.FrontPhoto)
            .NotNull().WithMessage("Front photo is required.")
            .Must(f => AllowedMimeTypes.Contains(f.ContentType))
            .WithMessage("Front photo must be image/jpeg, image/png, or image/webp.")
            .Must(f => f.Length > 0 && f.Length <= MaxBytes)
            .WithMessage("Front photo must be between 1 byte and 10 MB.");

        When(x => x.BackPhoto is not null, () =>
        {
            RuleFor(x => x.BackPhoto!)
                .Must(f => AllowedMimeTypes.Contains(f.ContentType))
                .WithMessage("Back photo must be image/jpeg, image/png, or image/webp.")
                .Must(f => f.Length > 0 && f.Length <= MaxBytes)
                .WithMessage("Back photo must be between 1 byte and 10 MB.");
        });

        RuleFor(x => x.Notes)
            .MaximumLength(500).When(x => x.Notes is not null)
            .WithMessage("Notes must not exceed 500 characters.");
    }
}
