using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.Utilities.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Inspections.Dtos;

namespace operations.Application.Warehouse.Inspections.Commands.UploadInspectionPhoto;

// ── Upload Inspection Photo ────────────────────────────────────────────────────

/// <summary>
/// Saves an uploaded photo to the file store and records a GarmentInspectionPhoto row.
/// </summary>
public sealed record UploadInspectionPhotoCommand(
    Guid        InspectionId,
    IFormFile   File,
    string      View,
    bool        IsPrimary,
    Guid?       ActorId) : ICommand<InspectionPhotoDto>;

public sealed class UploadInspectionPhotoCommandHandler
    : ICommandHandler<UploadInspectionPhotoCommand, InspectionPhotoDto>
{
    private readonly IOperationsDbContext   _db;
    private readonly ICurrentUser          _user;
    private readonly IFileStorageProvider  _storage;

    public UploadInspectionPhotoCommandHandler(
        IOperationsDbContext db, ICurrentUser user, IFileStorageProvider storage)
    {
        _db      = db;
        _user    = user;
        _storage = storage;
    }

    public async Task<InspectionPhotoDto> HandleAsync(
        UploadInspectionPhotoCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();

        // Verify the inspection belongs to this brand (RLS ensures brand isolation)
        var inspection = await _db.GarmentInspections
            .FirstOrDefaultAsync(i => i.Id == command.InspectionId && i.BrandId == brandId, cancellationToken)
            ?? throw new KeyNotFoundException($"Inspection {command.InspectionId} not found.");

        var now = DateTimeOffset.UtcNow;

        await using var stream = command.File.OpenReadStream();
        var key = await _storage.SaveAsync(
            stream, command.File.ContentType, "inspections", brandId, cancellationToken);

        var photo = new GarmentInspectionPhoto
        {
            Id           = Guid.NewGuid(),
            InspectionId = command.InspectionId,
            GarmentId    = inspection.GarmentId,
            BrandId      = brandId,
            S3Key        = key,
            View         = command.View,
            Annotations  = "[]",
            MimeType     = command.File.ContentType,
            Bytes        = (int)command.File.Length,
            IsCompressed = false,
            HasExif      = false,
            IsPrimary    = command.IsPrimary,
            CapturedAt   = now,
            CapturedBy   = command.ActorId,
            CreatedAt    = now,
            CreatedBy    = command.ActorId
        };

        _db.GarmentInspectionPhotos.Add(photo);
        await _db.SaveChangesAsync(cancellationToken);

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
