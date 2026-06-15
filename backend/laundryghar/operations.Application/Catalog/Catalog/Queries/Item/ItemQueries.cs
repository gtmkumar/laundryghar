using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Commands.Item;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Catalog.Queries.Item;

public sealed record GetItemsQuery(int Page, int PageSize, Guid? ItemGroupId) : IQuery<PaginatedList<ItemDto>>;

public sealed class GetItemsHandler : IQueryHandler<GetItemsQuery, PaginatedList<ItemDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetItemsHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<ItemDto>> HandleAsync(GetItemsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query = _db.Items.Where(x => x.BrandId == brandId);
        if (q.ItemGroupId.HasValue) query = query.Where(x => x.ItemGroupId == q.ItemGroupId.Value);
        return PaginatedList<ItemDto>.CreateAsync(
            query.OrderBy(x => x.DisplayOrder).Select(x => CreateItemHandler.ToDto(x)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetItemByIdQuery(Guid Id) : IQuery<ItemDto?>;

public sealed class GetItemByIdHandler : IQueryHandler<GetItemByIdQuery, ItemDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetItemByIdHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ItemDto?> HandleAsync(GetItemByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Items
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : CreateItemHandler.ToDto(e);
    }
}

// ── Stream an item's image (by item id) ───────────────────────────────────────

/// <summary>Result of fetching an image stream — carries stream + MIME type for the response.</summary>
public sealed record ItemImageStreamResult(Stream Stream, string ContentType, string FileName);

public sealed record GetItemImageStreamQuery(Guid ItemId) : IQuery<ItemImageStreamResult?>;

public sealed class GetItemImageStreamHandler : IQueryHandler<GetItemImageStreamQuery, ItemImageStreamResult?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser         _user;
    private readonly IFileStorageProvider _storage;

    public GetItemImageStreamHandler(IOperationsDbContext db, ICurrentUser user, IFileStorageProvider storage)
    {
        _db      = db;
        _user    = user;
        _storage = storage;
    }

    public async Task<ItemImageStreamResult?> HandleAsync(GetItemImageStreamQuery q, CancellationToken ct)
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
