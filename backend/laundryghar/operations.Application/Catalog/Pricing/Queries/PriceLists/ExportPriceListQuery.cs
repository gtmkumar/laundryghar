using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Pricing.Queries.PriceLists;

/// <summary>
/// Exports a price list to the flat import template layout (GH #24), so an admin can download →
/// edit → re-upload through the same import pipeline. Columns round-trip with
/// <see cref="operations.Application.Catalog.Catalog.Import.ImportFileParser"/>:
/// <c>Code,Name,Category,Status,TAT,Tax%</c> then one column per service (base price) plus a
/// <c>Service:Fabric</c> column for each fabric-specific row present in the list. Rows are the list's
/// active price_list_items grouped by item. Returns null when the list does not exist for the brand.
/// </summary>
public sealed record ExportPriceListQuery(Guid PriceListId, string Format) : IQuery<ImportTemplateFile?>;

public sealed class ExportPriceListHandler : IQueryHandler<ExportPriceListQuery, ImportTemplateFile?>
{
    private static readonly string[] FixedHeaders = ["Code", "Name", "Category", "Status", "TAT", "Tax%"];

    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public ExportPriceListHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ImportTemplateFile?> HandleAsync(ExportPriceListQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var list = await _db.PriceLists.AsNoTracking()
            .Where(pl => pl.Id == q.PriceListId && pl.BrandId == brandId && pl.DeletedAt == null)
            .Select(pl => new { pl.Id, pl.Code })
            .FirstOrDefaultAsync(ct);
        if (list is null) return null;

        // Active rows joined to item/service/fabric — the join emits null names for missing refs.
        var rows = await _db.PriceListItems.AsNoTracking()
            .Where(pi => pi.PriceListId == list.Id && pi.IsActive && pi.Status == "active")
            .Select(pi => new ExportRow(
                pi.ItemId,
                pi.Item.Code,
                pi.Item.Name,
                pi.Item.Status,
                pi.Item.TatHours,
                pi.Item.ItemGroupId,
                pi.Item.ItemGroup != null ? pi.Item.ItemGroup.Name : null,
                pi.ServiceId,
                pi.Service.Name,
                pi.Service.DisplayOrder,
                pi.FabricTypeId,
                pi.FabricType != null ? pi.FabricType.Name : null,
                pi.BasePrice,
                pi.TaxRatePercent))
            .ToListAsync(ct);

        var (headers, matrix) = BuildGrid(rows);

        var xlsx = string.Equals(q.Format, "xlsx", StringComparison.OrdinalIgnoreCase);
        var slug = string.IsNullOrWhiteSpace(list.Code) ? list.Id.ToString() : list.Code;
        return xlsx
            ? BuildXlsx(headers, matrix, $"price-list-{slug}.xlsx")
            : BuildCsv(headers, matrix, $"price-list-{slug}.csv");
    }

    private sealed record ExportRow(
        Guid ItemId, string ItemCode, string ItemName, string ItemStatus, int? TatHours,
        Guid? ItemGroupId, string? CategoryName,
        Guid ServiceId, string ServiceName, short ServiceDisplayOrder,
        Guid? FabricTypeId, string? FabricName,
        decimal BasePrice, decimal TaxRatePercent);

    /// <summary>Turns the flat rows into a header list + one string-cell row per item, matching the
    /// import template column shape (fixed columns, then service base + Service:Fabric columns).</summary>
    private static (List<string> Headers, List<List<string>> Rows) BuildGrid(List<ExportRow> rows)
    {
        // Column ordering: services by (display order, name); within a service the base column first,
        // then its fabric columns alphabetically. Keyed by NAME so a re-import matches by name.
        var serviceOrder = rows
            .GroupBy(r => r.ServiceName)
            .Select(g => new
            {
                Service = g.Key,
                Order = g.Min(x => x.ServiceDisplayOrder),
                Fabrics = g.Where(x => x.FabricName is not null)
                           .Select(x => x.FabricName!)
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                           .ToList(),
            })
            .OrderBy(x => x.Order).ThenBy(x => x.Service, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var headers = new List<string>(FixedHeaders);
        // Each column carries the lookup key it maps a cell to: (serviceName, fabricName?).
        var columnKeys = new List<(string Service, string? Fabric)>();
        foreach (var s in serviceOrder)
        {
            headers.Add(s.Service);
            columnKeys.Add((s.Service, null));
            foreach (var fabric in s.Fabrics)
            {
                headers.Add($"{s.Service}:{fabric}");
                columnKeys.Add((s.Service, fabric));
            }
        }

        var outRows = new List<List<string>>();
        foreach (var item in rows.GroupBy(r => r.ItemId))
        {
            var first = item.First();
            // Flat format carries a single Tax% per item; emit the max tax across the item's rows
            // (import applies one row-level tax to every price it touches).
            var tax = item.Max(x => x.TaxRatePercent);

            var cells = new List<string>
            {
                first.ItemCode,
                first.ItemName,
                first.CategoryName ?? string.Empty,
                first.ItemStatus,
                first.TatHours?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                tax == 0m ? string.Empty : tax.ToString(CultureInfo.InvariantCulture),
            };

            foreach (var key in columnKeys)
            {
                var cell = item.FirstOrDefault(x =>
                    string.Equals(x.ServiceName, key.Service, StringComparison.OrdinalIgnoreCase) &&
                    (key.Fabric is null
                        ? x.FabricTypeId is null
                        : string.Equals(x.FabricName, key.Fabric, StringComparison.OrdinalIgnoreCase)));
                cells.Add(cell is null ? string.Empty : cell.BasePrice.ToString(CultureInfo.InvariantCulture));
            }

            outRows.Add(cells);
        }

        return (headers, outRows);
    }

    private static ImportTemplateFile BuildCsv(List<string> headers, List<List<string>> rows, string fileName)
    {
        using var sw = new StringWriter(CultureInfo.InvariantCulture);
        using (var csv = new CsvWriter(sw, CultureInfo.InvariantCulture))
        {
            foreach (var h in headers) csv.WriteField(h);
            csv.NextRecord();
            foreach (var row in rows)
            {
                foreach (var v in row) csv.WriteField(v);
                csv.NextRecord();
            }
        }
        var bytes = Encoding.UTF8.GetBytes(sw.ToString());
        return new ImportTemplateFile(bytes, "text/csv", fileName);
    }

    private static ImportTemplateFile BuildXlsx(List<string> headers, List<List<string>> rows, string fileName)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Items");
        for (var c = 0; c < headers.Count; c++)
        {
            ws.Cell(1, c + 1).Value = headers[c];
            ws.Cell(1, c + 1).Style.Font.Bold = true;
        }
        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < rows[r].Count; c++)
                ws.Cell(r + 2, c + 1).Value = rows[r][c];
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return new ImportTemplateFile(
            ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }
}
