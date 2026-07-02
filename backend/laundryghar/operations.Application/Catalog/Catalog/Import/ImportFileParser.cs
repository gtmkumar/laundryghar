using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using operations.Application.Catalog.Catalog.Dtos;

namespace operations.Application.Catalog.Catalog.Import;

/// <summary>
/// Parses a bulk-import file (CSV or XLSX) into normalized <see cref="ImportItemRow"/>s plus per-row
/// warnings, with no database access. Two layouts are supported:
///
/// <list type="bullet">
///   <item><b>flat</b> — a CSV or the first sheet of an XLSX. Header
///     <c>Code,Name,Category,Status,TAT,Tax%</c> (all but Code/Name optional, case-insensitive) followed
///     by one column per service. A <c>Service:Fabric</c> header (e.g. "Dry Clean:Silk") carries a
///     fabric-variant price.</item>
///   <item><b>legacy_workbook</b> — an XLSX where each sheet name is a service and sheets carry the
///     "Item list for &lt;Category&gt; | Price Per Pc" wide layout (up to five side-by-side category
///     pairs) or the single "Item list | Price Per kg" layout. The same item name across sheets folds
///     into one item with several service prices. Item codes are slugged from the item name.</item>
/// </list>
/// Sheets that match neither header pattern are skipped and reported as row warnings.
/// </summary>
public static class ImportFileParser
{
    public const string LayoutFlat = "flat";
    public const string LayoutLegacy = "legacy_workbook";

    /// <summary>Normalized parse output: the detected layout, the rows, and any per-row warnings.</summary>
    public sealed record ParsedImport(string Layout, IReadOnlyList<ImportItemRow> Rows, IReadOnlyList<ImportRowError> Errors);

    private static readonly HashSet<string> FixedHeaders =
        new(StringComparer.OrdinalIgnoreCase) { "code", "name", "category", "status", "tat", "tat hours", "tathours", "tax%", "tax %", "tax" };

    /// <summary>
    /// Parses <paramref name="content"/>. <paramref name="fileName"/> disambiguates CSV from XLSX by
    /// extension; the ZIP (PK) magic bytes are a fallback when the extension is missing or wrong.
    /// The stream is read from its current position and left open.
    /// </summary>
    public static ParsedImport Parse(Stream content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();

        // Buffer so we can peek the magic bytes and still hand a seekable stream to the reader.
        var buffer = new MemoryStream();
        content.CopyTo(buffer);
        buffer.Position = 0;

        var isXlsx = ext is ".xlsx" or ".xlsm" or ".xls" || HasZipMagic(buffer);
        buffer.Position = 0;

        return isXlsx ? ParseXlsx(buffer) : ParseCsv(buffer);
    }

    private static bool HasZipMagic(Stream s)
    {
        Span<byte> head = stackalloc byte[2];
        var read = s.Read(head);
        return read == 2 && head[0] == 0x50 && head[1] == 0x4B; // "PK"
    }

    // ── Flat CSV ───────────────────────────────────────────────────────────────

    private static ParsedImport ParseCsv(Stream s)
    {
        var records = new List<string[]>();
        using (var reader = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
        using (var parser = new CsvParser(reader, CultureInfo.InvariantCulture))
        {
            while (parser.Read())
            {
                if (parser.Record is { } rec) records.Add(rec);
            }
        }

        var errors = new List<ImportRowError>();
        if (records.Count == 0)
        {
            errors.Add(new ImportRowError(0, "The file is empty."));
            return new ParsedImport(LayoutFlat, [], errors);
        }

        var header = records[0];
        var dataRows = records.Skip(1).Select((cells, i) => (cells, line: i + 2)); // header is line 1
        var rows = ParseFlatRows(header, dataRows, sheet: null, errors);
        return new ParsedImport(LayoutFlat, rows, errors);
    }

    // ── XLSX (flat first sheet OR legacy workbook) ───────────────────────────────

    private static ParsedImport ParseXlsx(Stream s)
    {
        using var wb = new XLWorkbook(s);
        var first = wb.Worksheets.FirstOrDefault();
        if (first is null)
            return new ParsedImport(LayoutFlat, [], [new ImportRowError(0, "The workbook has no sheets.")]);

        // Flat when the first sheet's header row carries both a Code and a Name column.
        var firstHeaderRow = FindFlatHeaderRow(first);
        if (firstHeaderRow is { } hr)
            return ParseFlatSheet(first, hr);

        return ParseLegacyWorkbook(wb);
    }

    private static int? FindFlatHeaderRow(IXLWorksheet ws)
    {
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var r = 1; r <= Math.Min(lastRow, 10); r++)
        {
            var headers = new List<string>();
            for (var c = 1; c <= lastCol; c++) headers.Add(CellString(ws, r, c));
            var hasCode = headers.Any(h => string.Equals(h.Trim(), "code", StringComparison.OrdinalIgnoreCase));
            var hasName = headers.Any(h => string.Equals(h.Trim(), "name", StringComparison.OrdinalIgnoreCase));
            if (hasCode && hasName) return r;
        }
        return null;
    }

    private static ParsedImport ParseFlatSheet(IXLWorksheet ws, int headerRow)
    {
        var errors = new List<ImportRowError>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        var header = new string[lastCol];
        for (var c = 1; c <= lastCol; c++) header[c - 1] = CellString(ws, headerRow, c);

        var data = new List<(string[] cells, int line)>();
        for (var r = headerRow + 1; r <= lastRow; r++)
        {
            var cells = new string[lastCol];
            var anything = false;
            for (var c = 1; c <= lastCol; c++)
            {
                cells[c - 1] = CellString(ws, r, c);
                if (cells[c - 1].Length > 0) anything = true;
            }
            if (anything) data.Add((cells, r));
        }

        var rows = ParseFlatRows(header, data, ws.Name, errors);
        return new ParsedImport(LayoutFlat, rows, errors);
    }

    /// <summary>Shared flat-layout row builder for both CSV and the first sheet of an XLSX.</summary>
    private static List<ImportItemRow> ParseFlatRows(
        string[] header, IEnumerable<(string[] cells, int line)> dataRows, string? sheet, List<ImportRowError> errors)
    {
        // Resolve fixed columns and service/fabric columns from the header.
        int? codeCol = null, nameCol = null, categoryCol = null, statusCol = null, tatCol = null, taxCol = null;
        // serviceName → (baseColIndex?, list of (fabric, colIndex))
        var serviceCols = new Dictionary<string, (int? BaseCol, List<(string Fabric, int Col)> Fabrics)>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < header.Length; i++)
        {
            var h = header[i]?.Trim() ?? string.Empty;
            if (h.Length == 0) continue;
            var hl = h.ToLowerInvariant();
            switch (hl)
            {
                case "code": codeCol = i; continue;
                case "name": nameCol = i; continue;
                case "category": categoryCol = i; continue;
                case "status": statusCol = i; continue;
                case "tat" or "tat hours" or "tathours": tatCol = i; continue;
                case "tax%" or "tax %" or "tax": taxCol = i; continue;
            }
            if (FixedHeaders.Contains(hl)) continue;

            // Service column — "Service" (base) or "Service:Fabric" (fabric variant).
            var colonIdx = h.IndexOf(':');
            if (colonIdx > 0)
            {
                var svc = h[..colonIdx].Trim();
                var fabric = h[(colonIdx + 1)..].Trim();
                if (svc.Length == 0 || fabric.Length == 0) continue;
                var entry = serviceCols.TryGetValue(svc, out var e) ? e : (null, new List<(string, int)>());
                entry.Item2.Add((fabric, i));
                serviceCols[svc] = entry;
            }
            else
            {
                var entry = serviceCols.TryGetValue(h, out var e) ? e : ((int?)null, new List<(string, int)>());
                entry.Item1 = i;
                serviceCols[h] = entry;
            }
        }

        var rows = new List<ImportItemRow>();
        foreach (var (cells, line) in dataRows)
        {
            string Cell(int? idx) => idx is { } j && j < cells.Length ? cells[j].Trim() : string.Empty;

            var name = Cell(nameCol);
            var code = Cell(codeCol);
            if (name.Length == 0)
            {
                errors.Add(new ImportRowError(line, "Name is required — row skipped.", sheet));
                continue;
            }
            if (code.Length == 0) code = SlugifyCode(name);

            var category = Cell(categoryCol);
            var status = Cell(statusCol);
            var tatText = Cell(tatCol);
            int? tat = null;
            if (tatText.Length > 0 && int.TryParse(tatText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t)) tat = t;

            decimal? tax = null;
            var taxText = Cell(taxCol).TrimEnd('%').Trim();
            if (taxText.Length > 0 && decimal.TryParse(taxText, NumberStyles.Number, CultureInfo.InvariantCulture, out var tx)) tax = tx;

            var servicePrices = new List<ImportItemServicePrice>();
            foreach (var (svc, (baseCol, fabrics)) in serviceCols)
            {
                decimal? basePrice = null;
                var baseText = Cell(baseCol);
                if (baseText.Length > 0)
                {
                    if (decimal.TryParse(baseText, NumberStyles.Number, CultureInfo.InvariantCulture, out var bp)) basePrice = bp;
                    else errors.Add(new ImportRowError(line, $"Service '{svc}' price '{baseText}' is not a number — ignored.", sheet));
                }

                var fabricPrices = new List<ImportItemFabricPrice>();
                foreach (var (fabric, col) in fabrics)
                {
                    var ft = Cell(col);
                    if (ft.Length == 0) continue;
                    if (decimal.TryParse(ft, NumberStyles.Number, CultureInfo.InvariantCulture, out var fp))
                        fabricPrices.Add(new ImportItemFabricPrice(fabric, fp));
                    else
                        errors.Add(new ImportRowError(line, $"Service '{svc}' fabric '{fabric}' price '{ft}' is not a number — ignored.", sheet));
                }

                if (basePrice is not null || fabricPrices.Count > 0)
                    servicePrices.Add(new ImportItemServicePrice(svc, basePrice, fabricPrices.Count > 0 ? fabricPrices : null));
            }

            rows.Add(new ImportItemRow(
                code, name,
                category.Length > 0 ? category : null,
                status.Length > 0 ? status : null,
                tat, servicePrices, tax));
        }

        return rows;
    }

    // ── Legacy workbook ──────────────────────────────────────────────────────────

    private static ParsedImport ParseLegacyWorkbook(XLWorkbook wb)
    {
        var errors = new List<ImportRowError>();
        // Fold items across sheets by slugged code. Preserve first-seen order for a stable preview.
        var byCode = new Dictionary<string, LegacyItem>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var ws in wb.Worksheets)
        {
            var service = ws.Name.Trim();
            var header = FindLegacyHeader(ws);
            if (header is null)
            {
                errors.Add(new ImportRowError(0,
                    $"Sheet '{ws.Name}' skipped: no recognised 'Item list' / 'Price Per Pc|Kg' header.", ws.Name));
                continue;
            }

            var (headerRow, pairs, perKg) = header.Value;
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;

            for (var r = headerRow + 1; r <= lastRow; r++)
            {
                foreach (var (nameCol, priceCol, category) in pairs)
                {
                    var name = CellString(ws, r, nameCol).Trim();
                    if (name.Length == 0) continue;

                    var priceText = CellString(ws, r, priceCol).Trim();
                    if (priceText.Length == 0) continue;
                    if (!decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
                    {
                        errors.Add(new ImportRowError(r, $"Item '{name}' price '{priceText}' is not a number — ignored.", ws.Name));
                        continue;
                    }

                    var code = SlugifyCode(name);
                    if (!byCode.TryGetValue(code, out var item))
                    {
                        item = new LegacyItem(code, name, category);
                        byCode[code] = item;
                        order.Add(code);
                    }
                    else if (item.Category is null && category is not null)
                    {
                        item.Category = category;
                    }

                    if (item.ServicePrices.TryGetValue(service, out var existing))
                    {
                        if (existing != price)
                            errors.Add(new ImportRowError(r,
                                $"Item '{name}' has conflicting '{service}' prices ({existing} vs {price}); kept {existing}.", ws.Name));
                    }
                    else
                    {
                        item.ServicePrices[service] = price;
                    }
                }
            }
            _ = perKg; // layout flag retained for readability; per-kg is just a single pair with null category
        }

        var rows = order
            .Select(code => byCode[code])
            .Select(it => new ImportItemRow(
                it.Code, it.Name, it.Category, Status: null, TatHours: null,
                it.ServicePrices.Select(kv => new ImportItemServicePrice(kv.Key, kv.Value)).ToList(),
                TaxRatePercent: null))
            .ToList();

        return new ParsedImport(LayoutLegacy, rows, errors);
    }

    private sealed class LegacyItem(string code, string name, string? category)
    {
        public string Code { get; } = code;
        public string Name { get; } = name;
        public string? Category { get; set; } = category;
        public Dictionary<string, decimal> ServicePrices { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Locates the "Item list …" header row within a legacy sheet and returns the category pairs
    /// (name column, price column, category) plus whether the sheet is a per-kg layout. Returns null
    /// when the sheet matches no legacy header pattern.
    /// </summary>
    private static (int HeaderRow, List<(int NameCol, int PriceCol, string? Category)> Pairs, bool PerKg)? FindLegacyHeader(IXLWorksheet ws)
    {
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow == 0 || lastCol == 0) return null;

        for (var r = 1; r <= Math.Min(lastRow, 15); r++)
        {
            var pairs = new List<(int, int, string?)>();
            var perKg = false;
            for (var c = 1; c <= lastCol; c++)
            {
                var cell = CellString(ws, r, c).Trim();
                if (cell.Length == 0) continue;
                if (!cell.StartsWith("Item list", StringComparison.OrdinalIgnoreCase)) continue;

                // The very next column must be a "Price Per …" header for this to be a real pair.
                var priceHeader = c + 1 <= lastCol ? CellString(ws, r, c + 1).Trim() : string.Empty;
                if (!priceHeader.StartsWith("Price Per", StringComparison.OrdinalIgnoreCase)) continue;

                if (priceHeader.Contains("kg", StringComparison.OrdinalIgnoreCase)) perKg = true;

                // Category = text after "Item list for" (handles the "Item list forLuxury" no-space typo).
                string? category = null;
                const string prefix = "Item list for";
                if (cell.Length > prefix.Length && cell.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var cat = cell[prefix.Length..].Trim();
                    if (cat.Length > 0) category = cat;
                }
                pairs.Add((c, c + 1, category));
            }

            if (pairs.Count > 0) return (r, pairs, perKg);
        }

        return null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static string CellString(IXLWorksheet ws, int row, int col)
    {
        var cell = ws.Cell(row, col);
        if (cell is null || cell.IsEmpty()) return string.Empty;
        var v = cell.Value;
        if (v.IsNumber)
        {
            var n = v.GetNumber();
            return n == Math.Floor(n)
                ? ((long)n).ToString(CultureInfo.InvariantCulture)
                : n.ToString(CultureInfo.InvariantCulture);
        }
        return cell.GetString().Trim();
    }

    /// <summary>Slugs an item name into a stable code: uppercase, non-alphanumerics → '-', collapsed and trimmed.</summary>
    public static string SlugifyCode(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var sb = new StringBuilder(name.Length);
        var lastDash = false;
        foreach (var ch in name.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastDash = false;
            }
            else if (!lastDash)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        return sb.ToString().Trim('-');
    }
}
