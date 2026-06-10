using FluentValidation;
using laundryghar.ServiceDefaults.Storage;
using laundryghar.Warehouse.Application.Inspections.Dtos;
using MediatR;

namespace laundryghar.Warehouse.Application.Inspections.Commands;

// ── Upload Inspection Photo ────────────────────────────────────────────────────

/// <summary>
/// Saves an uploaded photo to the file store and records a GarmentInspectionPhoto row.
/// </summary>
public sealed record UploadInspectionPhotoCommand(
    Guid        InspectionId,
    IFormFile   File,
    string      View,
    bool        IsPrimary,
    Guid?       ActorId) : IRequest<InspectionPhotoDto>;

public sealed class UploadInspectionPhotoHandler
    : IRequestHandler<UploadInspectionPhotoCommand, InspectionPhotoDto>
{
    private readonly LaundryGharDbContext   _db;
    private readonly ICurrentUser          _user;
    private readonly IFileStorageProvider  _storage;

    public UploadInspectionPhotoHandler(
        LaundryGharDbContext db, ICurrentUser user, IFileStorageProvider storage)
    {
        _db      = db;
        _user    = user;
        _storage = storage;
    }

    public async Task<InspectionPhotoDto> Handle(
        UploadInspectionPhotoCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        // Verify the inspection belongs to this brand (RLS ensures brand isolation)
        var inspection = await _db.GarmentInspections
            .FirstOrDefaultAsync(i => i.Id == cmd.InspectionId && i.BrandId == brandId, ct)
            ?? throw new KeyNotFoundException($"Inspection {cmd.InspectionId} not found.");

        var now = DateTimeOffset.UtcNow;

        await using var stream = cmd.File.OpenReadStream();
        var key = await _storage.SaveAsync(
            stream, cmd.File.ContentType, "inspections", brandId, ct);

        var photo = new GarmentInspectionPhoto
        {
            Id           = Guid.NewGuid(),
            InspectionId = cmd.InspectionId,
            GarmentId    = inspection.GarmentId,
            BrandId      = brandId,
            S3Key        = key,
            View         = cmd.View,
            Annotations  = "[]",
            MimeType     = cmd.File.ContentType,
            Bytes        = (int)cmd.File.Length,
            IsCompressed = false,
            HasExif      = false,
            IsPrimary    = cmd.IsPrimary,
            CapturedAt   = now,
            CapturedBy   = cmd.ActorId,
            CreatedAt    = now,
            CreatedBy    = cmd.ActorId
        };

        _db.GarmentInspectionPhotos.Add(photo);
        await _db.SaveChangesAsync(ct);

        return new InspectionPhotoDto(
            photo.Id, photo.S3Key, photo.View,
            photo.MimeType, photo.IsPrimary, photo.CreatedAt);
    }
}

public sealed class UploadInspectionPhotoValidator
    : AbstractValidator<UploadInspectionPhotoCommand>
{
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };

    private const long MaxBytes = 10 * 1024 * 1024; // 10 MB

    private static readonly string[] AllowedViews =
    [
        "front", "back", "left", "right", "top",
        "bottom", "closeup", "damage", "tag", "overall"
    ];

    public UploadInspectionPhotoValidator()
    {
        RuleFor(x => x.InspectionId).NotEmpty();

        RuleFor(x => x.View)
            .Must(v => AllowedViews.Contains(v))
            .WithMessage($"View must be one of: {string.Join(", ", AllowedViews)}.");

        RuleFor(x => x.File)
            .NotNull().WithMessage("A photo file is required.")
            .Must(f => AllowedMimeTypes.Contains(f.ContentType))
            .WithMessage("Photo must be image/jpeg, image/png, or image/webp.")
            .Must(f => f.Length <= MaxBytes)
            .WithMessage("Photo must be ≤ 10 MB.");
    }
}
