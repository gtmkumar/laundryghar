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
            .WithMessage("Image must be ≤ 5 MB.");
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
