using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Catalog.Queries.Item;

/// <summary>
/// Generates the flat import template (header + one example row) with one column per active brand
/// service, so an admin can fill in items and re-upload. Emits CSV or an XLSX workbook.
/// </summary>
public sealed record GetImportTemplateQuery(string Format) : IQuery<ImportTemplateFile>;

public sealed class GetImportTemplateHandler : IQueryHandler<GetImportTemplateQuery, ImportTemplateFile>
{
    private static readonly string[] FixedHeaders = ["Code", "Name", "Category", "Status", "TAT", "Tax%"];

    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetImportTemplateHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ImportTemplateFile> HandleAsync(GetImportTemplateQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var serviceNames = await _db.Services.AsNoTracking()
            .Where(s => s.BrandId == brandId && s.DeletedAt == null && s.Status == "active")
            .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
            .Select(s => s.Name)
            .ToListAsync(ct);

        var headers = FixedHeaders.Concat(serviceNames).ToList();

        // One illustrative row so the shape is unambiguous.
        var example = new List<string> { "SHIRT", "Shirt", "Mens", "active", "24", "5" };
        example.AddRange(serviceNames.Select(_ => "49"));

        var xlsx = string.Equals(q.Format, "xlsx", StringComparison.OrdinalIgnoreCase);
        return xlsx
            ? BuildXlsx(headers, example)
            : BuildCsv(headers, example);
    }

    private static ImportTemplateFile BuildCsv(List<string> headers, List<string> example)
    {
        using var sw = new StringWriter(CultureInfo.InvariantCulture);
        using (var csv = new CsvWriter(sw, CultureInfo.InvariantCulture))
        {
            foreach (var h in headers) csv.WriteField(h);
            csv.NextRecord();
            foreach (var v in example) csv.WriteField(v);
            csv.NextRecord();
        }
        var bytes = Encoding.UTF8.GetBytes(sw.ToString());
        return new ImportTemplateFile(bytes, "text/csv", "items-import-template.csv");
    }

    private static ImportTemplateFile BuildXlsx(List<string> headers, List<string> example)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Items");
        for (var c = 0; c < headers.Count; c++)
        {
            ws.Cell(1, c + 1).Value = headers[c];
            ws.Cell(1, c + 1).Style.Font.Bold = true;
            ws.Cell(2, c + 1).Value = example[c];
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return new ImportTemplateFile(
            ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "items-import-template.xlsx");
    }
}
