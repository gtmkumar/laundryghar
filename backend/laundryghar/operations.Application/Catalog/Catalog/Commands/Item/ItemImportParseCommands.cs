using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.AspNetCore.Http;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Catalog.Import;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Catalog.Commands.Item;

/// <summary>
/// Server-side dry-run for the import wizard. Parses the uploaded CSV/XLSX, validates the normalized
/// rows against the brand's catalog, and returns a diff report (create/update counts, projected price
/// changes vs the working list, unknown services/categories, per-row warnings). The only write is
/// persisting the original file via <see cref="IFileStorageProvider"/> under the "imports" area so the
/// commit step can reference it — no catalog rows are touched here. The actual parse/validate/diff work
/// lives in <see cref="ImportParseService"/>, shared with the Google Sheet import flow.
/// </summary>
public sealed record ParseImportCommand(IFormFile File, Guid? ActorId) : ICommand<ParseImportResult>;

public sealed class ParseImportHandler : ICommandHandler<ParseImportCommand, ParseImportResult>
{
    private const long MaxBytes = 10 * 1024 * 1024; // 10 MB
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".csv", ".xlsx", ".xlsm", ".xls" };

    private readonly ImportParseService _parse;

    public ParseImportHandler(ImportParseService parse) => _parse = parse;

    public async Task<ParseImportResult> HandleAsync(ParseImportCommand cmd, CancellationToken ct)
    {
        // Guard inline: the dispatcher pipeline (and thus command validators) is not wired, and the
        // multipart IFormFile is not a bound DTO that ValidationFilter<T> could target.
        var file = cmd.File;
        if (file is null || file.Length == 0)
            throw new ValidationException(new Dictionary<string, string[]> { ["file"] = ["A non-empty file is required."] });
        if (file.Length > MaxBytes)
            throw new ValidationException(new Dictionary<string, string[]> { ["file"] = ["The file must be <= 10 MB."] });
        if (!AllowedExtensions.Contains(Path.GetExtension(file.FileName ?? string.Empty)))
            throw new ValidationException(new Dictionary<string, string[]> { ["file"] = ["The file must be a .csv or .xlsx workbook."] });

        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        await using var upload = file.OpenReadStream();
        return await _parse.ParseAndReportAsync(
            upload, file.FileName ?? string.Empty, contentType, sourceUrl: null, addTemplateHintIfNoRows: false, ct);
    }
}
