using laundryghar.Catalog.Infrastructure.Auth;
using laundryghar.Catalog.Infrastructure.Services;
using FluentValidation;
using laundryghar.Catalog.Application.Catalog.Dtos;
using laundryghar.ServiceDefaults.Storage;
using MediatR;

namespace laundryghar.Catalog.Application.Catalog.Commands;

// ── Upload Item Image ─────────────────────────────────────────────────────────

/// <summary>
/// Saves an uploaded image to the file store and points items.image_url at the
/// storage key. Replacing an image deletes the previous stored object.
/// The image is served via GET /api/v1/admin/items/{id}/image (never by raw key).
/// </summary>
public sealed record UploadItemImageCommand(
    Guid      ItemId,
    IFormFile File,
    Guid?     ActorId) : IRequest<ItemDto?>;

public sealed class UploadItemImageHandler : IRequestHandler<UploadItemImageCommand, ItemDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    private readonly IFileStorageProvider _storage;

    public UploadItemImageHandler(LaundryGharDbContext db, ICurrentUser user, IFileStorageProvider storage)
    {
        _db      = db;
        _user    = user;
        _storage = storage;
    }

    public async Task<ItemDto?> Handle(UploadItemImageCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Items
            .FirstOrDefaultAsync(x => x.Id == cmd.ItemId && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return null;

        await using var stream = cmd.File.OpenReadStream();
        var key = await _storage.SaveAsync(stream, cmd.File.ContentType, "catalog/items", brandId, ct);

        var oldKey = e.ImageUrl;
        e.ImageUrl  = key;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;
        e.Version++;

        await _db.SaveChangesAsync(ct);

        // Only delete after the DB points at the new key; externally-set http(s)
        // URLs are not storage keys and must not be passed to the provider.
        if (!string.IsNullOrEmpty(oldKey) && !oldKey.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            await _storage.DeleteAsync(oldKey, ct);

        return CreateItemHandler.ToDto(e);
    }
}

public sealed class UploadItemImageValidator : AbstractValidator<UploadItemImageCommand>
{
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };

    private const long MaxBytes = 5 * 1024 * 1024; // 5 MB

    public UploadItemImageValidator()
    {
        RuleFor(x => x.ItemId).NotEmpty();

        RuleFor(x => x.File)
            .NotNull().WithMessage("An image file is required.")
            .Must(f => AllowedMimeTypes.Contains(f.ContentType))
            .WithMessage("Image must be image/jpeg, image/png, or image/webp.")
            .Must(f => f.Length <= MaxBytes)
            .WithMessage("Image must be <= 5 MB.")
            // Magic-byte validation: reject files that lie about their ContentType.
            // Mirrors the pattern in laundryghar.Logistics RiderInspectionCommands.ValidateImageBytes
            // but scoped locally so there is no cross-project dependency.
            .Must(f =>
            {
                try
                {
                    using var stream = f.OpenReadStream();
                    return ImageMagicBytes.IsValidImage(stream);
                }
                catch
                {
                    return false;
                }
            })
            .WithMessage("The uploaded file is not a valid JPEG, PNG, or WebP image.");
    }
}

/// <summary>
/// Magic-byte validation for uploaded images.
/// Checks the first 12 bytes of the stream against JPEG, PNG, and WebP signatures.
/// Internal to the Catalog service — does NOT depend on laundryghar.Logistics.
/// </summary>
internal static class ImageMagicBytes
{
    /// <summary>
    /// Returns true when the stream starts with a recognised image magic-byte sequence.
    /// The stream position is reset to 0 after the check.
    /// </summary>
    internal static bool IsValidImage(Stream stream)
    {
        Span<byte> header = stackalloc byte[12];
        stream.Position = 0;
        var read = stream.Read(header);
        stream.Position = 0;   // reset so the handler can re-read from the beginning

        if (read < 3) return false;

        // JPEG: FF D8 FF
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return true;

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (read >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return true;

        // WebP: RIFF????WEBP  (bytes 0-3 = 52 49 46 46, bytes 8-11 = 57 45 42 50)
        if (read >= 12
            && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
            && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
            return true;

        return false;
    }
}

// ── Delete Item Image ─────────────────────────────────────────────────────────

public sealed record DeleteItemImageCommand(Guid ItemId, Guid? ActorId) : IRequest<ItemDto?>;

public sealed class DeleteItemImageHandler : IRequestHandler<DeleteItemImageCommand, ItemDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    private readonly IFileStorageProvider _storage;

    public DeleteItemImageHandler(LaundryGharDbContext db, ICurrentUser user, IFileStorageProvider storage)
    {
        _db      = db;
        _user    = user;
        _storage = storage;
    }

    public async Task<ItemDto?> Handle(DeleteItemImageCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Items
            .FirstOrDefaultAsync(x => x.Id == cmd.ItemId && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return null;

        var oldKey = e.ImageUrl;
        e.ImageUrl  = null;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;
        e.Version++;

        await _db.SaveChangesAsync(ct);

        if (!string.IsNullOrEmpty(oldKey) && !oldKey.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            await _storage.DeleteAsync(oldKey, ct);

        return CreateItemHandler.ToDto(e);
    }
}
