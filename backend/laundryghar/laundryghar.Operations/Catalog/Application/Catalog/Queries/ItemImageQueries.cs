using laundryghar.Catalog.Infrastructure.Auth;
using laundryghar.Catalog.Infrastructure.Services;
using laundryghar.ServiceDefaults.Storage;
using MediatR;

namespace laundryghar.Catalog.Application.Catalog.Queries;

// ── Stream an item's image (by item id) ───────────────────────────────────────

/// <summary>Result of fetching an image stream — carries stream + MIME type for the response.</summary>
public sealed record ItemImageStreamResult(Stream Stream, string ContentType, string FileName);

public sealed record GetItemImageStreamQuery(Guid ItemId) : IRequest<ItemImageStreamResult?>;

public sealed class GetItemImageStreamHandler
    : IRequestHandler<GetItemImageStreamQuery, ItemImageStreamResult?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    private readonly IFileStorageProvider _storage;

    public GetItemImageStreamHandler(
        LaundryGharDbContext db, ICurrentUser user, IFileStorageProvider storage)
    {
        _db      = db;
        _user    = user;
        _storage = storage;
    }

    public async Task<ItemImageStreamResult?> Handle(
        GetItemImageStreamQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var key = await _db.Items
            .Where(i => i.Id == q.ItemId && i.BrandId == brandId && i.DeletedAt == null)
            .Select(i => i.ImageUrl)
            .FirstOrDefaultAsync(ct);

        // Externally-set http(s) URLs are not storage keys — the client should
        // load those directly; this endpoint only streams stored objects.
        if (string.IsNullOrEmpty(key) || key.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return null;

        var stream = await _storage.OpenReadAsync(key, ct);
        var ext    = key[(key.LastIndexOf('.') + 1)..];

        return new ItemImageStreamResult(stream, ContentTypeFromExtension(ext), $"item-{q.ItemId:N}.{ext}");
    }

    private static string ContentTypeFromExtension(string ext) => ext.ToLowerInvariant() switch
    {
        "jpg" or "jpeg" => "image/jpeg",
        "png"           => "image/png",
        "webp"          => "image/webp",
        "gif"           => "image/gif",
        _               => "application/octet-stream"
    };
}
