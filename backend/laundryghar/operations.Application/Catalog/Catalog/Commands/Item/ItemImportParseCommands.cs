using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Catalog.Import;
using operations.Application.Catalog.Pricing.Common;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Catalog.Commands.Item;

/// <summary>
/// Server-side dry-run for the import wizard. Parses the uploaded CSV/XLSX, validates the normalized
/// rows against the brand's catalog, and returns a diff report (create/update counts, projected price
/// changes vs the working list, unknown services/categories, per-row warnings). The only write is
/// persisting the original file via <see cref="IFileStorageProvider"/> under the "imports" area so the
/// commit step can reference it — no catalog rows are touched here.
/// </summary>
public sealed record ParseImportCommand(IFormFile File, Guid? ActorId) : ICommand<ParseImportResult>;

public sealed class ParseImportHandler : ICommandHandler<ParseImportCommand, ParseImportResult>
{
    private const int MaxPriceChanges = 500;

    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IFileStorageProvider _storage;

    public ParseImportHandler(IOperationsDbContext db, ICurrentUser user, IFileStorageProvider storage)
    {
        _db = db;
        _user = user;
        _storage = storage;
    }

    private const long MaxBytes = 10 * 1024 * 1024; // 10 MB
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".csv", ".xlsx", ".xlsm", ".xls" };

    public async Task<ParseImportResult> HandleAsync(ParseImportCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        // Guard inline: the dispatcher pipeline (and thus command validators) is not wired, and the
        // multipart IFormFile is not a bound DTO that ValidationFilter<T> could target.
        var file = cmd.File;
        if (file is null || file.Length == 0)
            throw new ValidationException(new Dictionary<string, string[]> { ["file"] = ["A non-empty file is required."] });
        if (file.Length > MaxBytes)
            throw new ValidationException(new Dictionary<string, string[]> { ["file"] = ["The file must be <= 10 MB."] });
        if (!AllowedExtensions.Contains(Path.GetExtension(file.FileName ?? string.Empty)))
            throw new ValidationException(new Dictionary<string, string[]> { ["file"] = ["The file must be a .csv or .xlsx workbook."] });

        // Buffer once: parsed for the report AND streamed to storage.
        using var buffer = new MemoryStream();
        await using (var upload = cmd.File.OpenReadStream())
            await upload.CopyToAsync(buffer, ct);

        buffer.Position = 0;
        var parsed = ImportFileParser.Parse(buffer, cmd.File.FileName);
        var rows = parsed.Rows;
        var rowErrors = new List<ImportRowError>(parsed.Errors);

        // ── Brand lookups for validation + diff ──────────────────────────────────
        var services = await _db.Services.AsNoTracking()
            .Where(s => s.BrandId == brandId && s.DeletedAt == null)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct);
        var svcByName = services
            .GroupBy(s => s.Name.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Id);

        var groups = await _db.ItemGroups.AsNoTracking()
            .Where(g => g.BrandId == brandId && g.DeletedAt == null)
            .Select(g => new { g.Name, g.Code })
            .ToListAsync(ct);
        bool GroupExists(string category)
        {
            var c = category.Trim().ToLowerInvariant();
            return groups.Any(g => g.Name.Trim().ToLowerInvariant() == c || g.Code.Trim().ToLowerInvariant() == c);
        }

        var fabrics = await _db.FabricTypes.AsNoTracking()
            .Where(f => f.BrandId == brandId && f.DeletedAt == null)
            .Select(f => new { f.Id, f.Name })
            .ToListAsync(ct);
        var fabricByName = fabrics
            .GroupBy(f => f.Name.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Id);

        var codes = rows.Select(r => r.Code).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        var existingItems = await _db.Items.AsNoTracking()
            .Where(i => i.BrandId == brandId && i.DeletedAt == null && codes.Contains(i.Code))
            .Select(i => new { i.Id, i.Code })
            .ToListAsync(ct);
        var itemIdByCode = existingItems
            .GroupBy(i => i.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        // Current working-list base prices, keyed by (itemId, serviceId, fabricTypeId?), for the diff.
        var workingListId = await WorkingPriceList.ResolveIdAsync(_db, brandId, ct);
        var currentPrices = new Dictionary<(Guid Item, Guid Service, Guid? Fabric), decimal>();
        if (workingListId is { } wl && existingItems.Count > 0)
        {
            var itemIds = existingItems.Select(i => i.Id).ToList();
            var priceRows = await _db.PriceListItems.AsNoTracking()
                .Where(p => p.PriceListId == wl && itemIds.Contains(p.ItemId) && p.IsActive)
                .Select(p => new { p.ItemId, p.ServiceId, p.FabricTypeId, p.BasePrice })
                .ToListAsync(ct);
            foreach (var p in priceRows)
                currentPrices[(p.ItemId, p.ServiceId, p.FabricTypeId)] = p.BasePrice;
        }

        // ── Build report ─────────────────────────────────────────────────────────
        int toCreate = 0, toUpdate = 0;
        var unknownServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unknownCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var priceChanges = new List<ImportPriceChange>();
        var truncated = false;

        void AddChange(ImportPriceChange change)
        {
            if (priceChanges.Count >= MaxPriceChanges) { truncated = true; return; }
            priceChanges.Add(change);
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var line = i + 1; // position in the normalized preview

            var known = itemIdByCode.TryGetValue(row.Code, out var itemId);
            if (known) toUpdate++; else toCreate++;

            if (!string.IsNullOrWhiteSpace(row.Category) && !GroupExists(row.Category))
                unknownCategories.Add(row.Category.Trim());

            foreach (var sp in row.ServicePrices)
            {
                if (string.IsNullOrWhiteSpace(sp.ServiceName)) continue;
                if (!svcByName.TryGetValue(sp.ServiceName.Trim().ToLowerInvariant(), out var serviceId))
                {
                    unknownServices.Add(sp.ServiceName.Trim());
                    continue;
                }

                if (sp.BasePrice is { } bp)
                {
                    decimal? old = known && currentPrices.TryGetValue((itemId, serviceId, null), out var ob) ? ob : null;
                    if (old != bp) AddChange(new ImportPriceChange(row.Code, row.Name, sp.ServiceName.Trim(), null, old, bp));
                }

                foreach (var fp in sp.FabricPrices ?? [])
                {
                    if (!fabricByName.TryGetValue(fp.FabricName.Trim().ToLowerInvariant(), out var fabricId))
                    {
                        rowErrors.Add(new ImportRowError(line, $"Item '{row.Code}': fabric '{fp.FabricName}' not found — price will be skipped."));
                        continue;
                    }
                    decimal? old = known && currentPrices.TryGetValue((itemId, serviceId, fabricId), out var of) ? of : null;
                    if (old != fp.Price) AddChange(new ImportPriceChange(row.Code, row.Name, sp.ServiceName.Trim(), fp.FabricName.Trim(), old, fp.Price));
                }
            }
        }

        // ── Store the original file (only real write here) ───────────────────────
        buffer.Position = 0;
        var contentType = string.IsNullOrWhiteSpace(cmd.File.ContentType) ? "application/octet-stream" : cmd.File.ContentType;
        var fileRef = await _storage.SaveAsync(buffer, contentType, "imports", brandId, ct);

        var report = new ImportReport(
            TotalRows: rows.Count,
            ToCreate: toCreate,
            ToUpdate: toUpdate,
            PriceChanges: priceChanges,
            PriceChangesTruncated: truncated,
            UnknownServices: unknownServices.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            UnknownCategories: unknownCategories.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            RowErrors: rowErrors);

        return new ParseImportResult(fileRef, parsed.Layout, rows, report);
    }
}
