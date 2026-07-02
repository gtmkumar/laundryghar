using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Pricing.Common;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Catalog.Import;

/// <summary>
/// Shared parse-and-diff engine behind the import wizard's dry-run step. Given a raw file stream
/// (a browser upload OR a fetched Google Sheet CSV), it parses the file into normalized rows,
/// validates them against the brand's catalog, persists the original bytes via
/// <see cref="IFileStorageProvider"/> (area "imports") for the later commit step, and returns the
/// diff report. This is the single code path both <c>ParseImportCommand</c> (file upload) and
/// <c>ParseGoogleSheetImportCommand</c> route through so the validation logic is never duplicated.
/// </summary>
public sealed class ImportParseService
{
    private const int MaxPriceChanges = 500;

    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IFileStorageProvider _storage;

    public ImportParseService(IOperationsDbContext db, ICurrentUser user, IFileStorageProvider storage)
    {
        _db = db;
        _user = user;
        _storage = storage;
    }

    /// <summary>
    /// Parses <paramref name="content"/> (read from its current position; left open), validates the
    /// rows against the current brand, stores the original bytes, and returns the diff report.
    /// </summary>
    /// <param name="content">Seekable-or-not readable stream of the source file. Buffered internally.</param>
    /// <param name="fileName">Original/synthetic file name — only its extension disambiguates CSV vs XLSX.</param>
    /// <param name="contentType">MIME type used both for the stored blob and its derived extension.</param>
    /// <param name="sourceUrl">Optional source URL (Google Sheet) echoed back in the result.</param>
    /// <param name="addTemplateHintIfNoRows">When true and nothing parsed, prepends a friendly
    /// "your header doesn't match the template" summary row error (used for the Google Sheet flow,
    /// where a wrong tab/sheet is the most common mistake).</param>
    public async Task<ParseImportResult> ParseAndReportAsync(
        Stream content,
        string fileName,
        string contentType,
        string? sourceUrl,
        bool addTemplateHintIfNoRows,
        CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        // Buffer once: parsed for the report AND streamed to storage.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);

        buffer.Position = 0;
        var parsed = ImportFileParser.Parse(buffer, fileName);
        var rows = parsed.Rows;
        var rowErrors = new List<ImportRowError>(parsed.Errors);

        if (addTemplateHintIfNoRows && rows.Count == 0)
        {
            rowErrors.Insert(0, new ImportRowError(0,
                "No importable rows were found. The first row must match the import template " +
                "(columns: Code, Name, Category, Status, TAT, Tax%, then one column per service). " +
                "Download the template from Items → Import → Download template, paste your data into it, " +
                "then share that sheet."));
        }

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
        var storedContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        var fileRef = await _storage.SaveAsync(buffer, storedContentType, "imports", brandId, ct);

        var report = new ImportReport(
            TotalRows: rows.Count,
            ToCreate: toCreate,
            ToUpdate: toUpdate,
            PriceChanges: priceChanges,
            PriceChangesTruncated: truncated,
            UnknownServices: unknownServices.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            UnknownCategories: unknownCategories.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            RowErrors: rowErrors);

        return new ParseImportResult(fileRef, parsed.Layout, rows, report, sourceUrl);
    }
}
