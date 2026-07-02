using System.Net;
using System.Text.RegularExpressions;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Catalog.Import;

namespace operations.Application.Catalog.Catalog.Commands.Item;

/// <summary>
/// Server-side dry-run for the "import from Google Sheet" wizard step. Fetches the sheet as CSV via
/// its public <c>export?format=csv</c> endpoint, then runs the SAME parse/validate/diff path as the
/// file upload (<see cref="ImportParseService"/>). No catalog rows are written — the only side effect
/// is storing the fetched CSV under the "imports" area so the commit step can reference it.
///
/// <para><b>SSRF hardening:</b> the URL host must be exactly <c>docs.google.com</c>; the named HTTP
/// client has auto-redirect disabled and redirects are followed manually (max 3 hops) only to hosts
/// under <c>docs.google.com</c> / <c>googleusercontent.com</c>. A redirect to a Google login page ⇒ a
/// typed "sheet is not link-shared" business error rather than a silent auth wall.</para>
/// </summary>
public sealed record ParseGoogleSheetImportCommand(string Url, string? Gid, Guid? ActorId)
    : ICommand<ParseImportResult>;

public sealed class ParseGoogleSheetImportHandler : ICommandHandler<ParseGoogleSheetImportCommand, ParseImportResult>
{
    /// <summary>Named client — configured with AllowAutoRedirect=false + a 15s timeout in Program.cs.</summary>
    public const string HttpClientName = "google-sheets";

    private const long MaxBytes = 10 * 1024 * 1024; // 10 MB response cap
    private const int MaxRedirects = 3;
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(15);

    private const string NotSharedMessage =
        "Sheet is not link-shared — set it to 'Anyone with the link can view'.";

    // Path shape: /spreadsheets/d/{id}/...  — id is Google's base64url-ish document key.
    private static readonly Regex SheetIdPattern =
        new(@"^/spreadsheets/d/(?<id>[A-Za-z0-9\-_]+)", RegexOptions.Compiled);
    private static readonly Regex GidPattern = new(@"gid=(?<gid>\d+)", RegexOptions.Compiled);
    private static readonly Regex DigitsOnly = new(@"^\d+$", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpFactory;
    private readonly ImportParseService _parse;

    public ParseGoogleSheetImportHandler(IHttpClientFactory httpFactory, ImportParseService parse)
    {
        _httpFactory = httpFactory;
        _parse = parse;
    }

    public async Task<ParseImportResult> HandleAsync(ParseGoogleSheetImportCommand cmd, CancellationToken ct)
    {
        var (sheetId, gid) = ParseAndValidateUrl(cmd.Url, cmd.Gid);

        var exportUri = new Uri(
            $"https://docs.google.com/spreadsheets/d/{sheetId}/export?format=csv&gid={gid}");

        // Bound the whole fetch (all redirect hops + body read) to ~15s, independent of the DB work.
        using var fetchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        fetchCts.CancelAfter(FetchTimeout);

        byte[] csv;
        try
        {
            csv = await FetchCsvAsync(exportUri, fetchCts.Token);
        }
        catch (OperationCanceledException) when (fetchCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new BusinessRuleException("Timed out fetching the Google Sheet. Please try again.");
        }

        using var stream = new MemoryStream(csv, writable: false);
        return await _parse.ParseAndReportAsync(
            stream,
            fileName: "google-sheet.csv",
            contentType: "text/csv",
            sourceUrl: cmd.Url,
            addTemplateHintIfNoRows: true,
            ct);
    }

    /// <summary>
    /// Validates the URL (host allow-list = SSRF guard) and resolves the target sheet id + gid.
    /// The explicit <paramref name="gid"/> parameter wins over any gid embedded in the URL.
    /// </summary>
    internal static (string SheetId, string Gid) ParseAndValidateUrl(string? url, string? gid)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            throw Invalid("url", "A Google Sheets URL is required.");

        if (uri.Scheme != Uri.UriSchemeHttps)
            throw Invalid("url", "The URL must be an https:// Google Sheets link.");

        // Host must be EXACTLY docs.google.com — this is the primary SSRF guard.
        if (!string.Equals(uri.Host, "docs.google.com", StringComparison.OrdinalIgnoreCase))
            throw Invalid("url", "Only docs.google.com share links are supported.");

        var m = SheetIdPattern.Match(uri.AbsolutePath);
        if (!m.Success)
            throw Invalid("url", "That doesn't look like a Google Sheets link (expected .../spreadsheets/d/<id>/...).");
        var sheetId = m.Groups["id"].Value;

        // Explicit gid wins; otherwise pull it from the URL fragment or query; default to the first tab.
        string resolvedGid;
        if (!string.IsNullOrWhiteSpace(gid))
        {
            if (!DigitsOnly.IsMatch(gid.Trim()))
                throw Invalid("gid", "gid must be a number (the sheet-tab id).");
            resolvedGid = gid.Trim();
        }
        else
        {
            var fromFragment = GidPattern.Match(uri.Fragment);
            var fromQuery = GidPattern.Match(uri.Query);
            resolvedGid = fromFragment.Success ? fromFragment.Groups["gid"].Value
                : fromQuery.Success ? fromQuery.Groups["gid"].Value
                : "0";
        }

        return (sheetId, resolvedGid);
    }

    private async Task<byte[]> FetchCsvAsync(Uri startUri, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient(HttpClientName);
        var current = startUri;

        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, current);
            req.Headers.Accept.ParseAdd("text/csv");

            var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            try
            {
                if (IsRedirect(resp.StatusCode))
                {
                    var location = resp.Headers.Location
                        ?? throw new BusinessRuleException("The sheet redirected without a target — it may not be shared.");
                    current = ResolveRedirect(current, location);
                    continue;
                }

                if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new BusinessRuleException(NotSharedMessage);
                if (resp.StatusCode == HttpStatusCode.NotFound)
                    throw new BusinessRuleException("Google Sheet not found — check the link.");
                if (!resp.IsSuccessStatusCode)
                    throw new BusinessRuleException($"Could not fetch the Google Sheet (HTTP {(int)resp.StatusCode}).");

                // An unshared sheet is answered with the HTML sign-in page, not CSV.
                var mediaType = resp.Content.Headers.ContentType?.MediaType;
                if (mediaType is not null && mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                    throw new BusinessRuleException(NotSharedMessage);

                if (resp.Content.Headers.ContentLength is > MaxBytes)
                    throw new BusinessRuleException("The Google Sheet is larger than 10 MB.");

                await using var body = await resp.Content.ReadAsStreamAsync(ct);
                var bytes = await ReadCappedAsync(body, MaxBytes, ct);

                // Content-type sniff can be absent/wrong; catch an HTML login body by its markup.
                if (LooksLikeHtml(bytes))
                    throw new BusinessRuleException(NotSharedMessage);

                return bytes;
            }
            finally
            {
                resp.Dispose();
            }
        }

        throw new BusinessRuleException("Too many redirects while fetching the Google Sheet — it may not be shared.");
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        (int)status is 301 or 302 or 303 or 307 or 308;

    /// <summary>Resolves the redirect target and enforces the host allow-list (SSRF guard on hops).</summary>
    private static Uri ResolveRedirect(Uri from, Uri location)
    {
        var target = location.IsAbsoluteUri ? location : new Uri(from, location);

        if (target.Scheme != Uri.UriSchemeHttps)
            throw new BusinessRuleException("The Google Sheet redirected to a non-https location — blocked.");

        if (IsGoogleLogin(target))
            throw new BusinessRuleException(NotSharedMessage);

        if (!IsAllowedFetchHost(target.Host))
            throw new BusinessRuleException("The Google Sheet redirected to an untrusted host — blocked.");

        return target;
    }

    // Allow docs.google.com and *.googleusercontent.com (where shared CSV exports are served from),
    // matched with a leading-dot suffix so "evildocs.google.com" can never slip through.
    private static bool IsAllowedFetchHost(string host) =>
        host.Equals("docs.google.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".docs.google.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("googleusercontent.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsGoogleLogin(Uri uri) =>
        uri.Host.Equals("accounts.google.com", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.EndsWith(".accounts.google.com", StringComparison.OrdinalIgnoreCase) ||
        uri.AbsolutePath.Contains("ServiceLogin", StringComparison.OrdinalIgnoreCase) ||
        uri.AbsolutePath.Contains("signin", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeHtml(byte[] bytes)
    {
        // Peek the first non-whitespace bytes; a login page starts with "<!DOCTYPE html" / "<html".
        var i = 0;
        while (i < bytes.Length && (bytes[i] == (byte)' ' || bytes[i] == (byte)'\n'
               || bytes[i] == (byte)'\r' || bytes[i] == (byte)'\t' || bytes[i] == 0xEF
               || bytes[i] == 0xBB || bytes[i] == 0xBF)) i++; // skip whitespace + UTF-8 BOM
        if (i >= bytes.Length || bytes[i] != (byte)'<') return false;

        var head = System.Text.Encoding.ASCII.GetString(bytes, i, Math.Min(64, bytes.Length - i));
        return head.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]> ReadCappedAsync(Stream source, long cap, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > cap)
                throw new BusinessRuleException("The Google Sheet is larger than 10 MB.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
