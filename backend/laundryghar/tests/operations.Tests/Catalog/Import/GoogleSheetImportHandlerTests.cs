using System.Net;
using System.Text;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Utilities.Exceptions;
using operations.Application.Catalog.Catalog.Commands.Item;
using operations.Application.Catalog.Catalog.Import;
using operations.Application.Common.Interfaces;
using Xunit;

namespace operations.Tests.Catalog.Import;

/// <summary>
/// End-to-end coverage for the Google Sheet import handler (#21): the fetch pipeline (redirect
/// following, host allow-list, login/HTML detection, size cap) and the happy path that flows the
/// fetched CSV through the shared <see cref="ImportParseService"/>. HTTP is faked via a scripted
/// <see cref="HttpMessageHandler"/>; the DB is the in-memory operations context.
/// </summary>
public class GoogleSheetImportHandlerTests
{
    private const string SheetUrl = "https://docs.google.com/spreadsheets/d/1AbC_dEf-123/edit#gid=0";

    // ── happy path ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Happy_path_follows_googleusercontent_redirect_and_parses_csv()
    {
        var (handler, raw, brandId) = NewHandler(req =>
        {
            // First hop: docs.google.com/export → 307 to the googleusercontent CSV host (allowed).
            if (req.RequestUri!.Host == "docs.google.com")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
                redirect.Headers.Location = new Uri("https://doc-0-sheets.googleusercontent.com/pub?id=1AbC");
                return redirect;
            }
            // Second hop: the actual CSV.
            return Csv("Code,Name,Category,Wash\nSHIRT,Shirt,Mens,30\n");
        });
        SeedService(raw, brandId, "Wash");
        await raw.SaveChangesAsync();

        var result = await handler.HandleAsync(
            new ParseGoogleSheetImportCommand(SheetUrl, null, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ImportFileParser.LayoutFlat, result.Layout);
        Assert.Equal(SheetUrl, result.SourceUrl);
        Assert.NotNull(result.FileRef);
        var row = Assert.Single(result.Rows);
        Assert.Equal("SHIRT", row.Code);
        Assert.Equal(1, result.Report.ToCreate);
        Assert.Empty(result.Report.UnknownServices); // "Wash" was seeded
        Assert.Contains(result.Report.PriceChanges, p => p is { ServiceName: "Wash", NewPrice: 30m });
    }

    [Fact]
    public async Task Header_not_matching_template_yields_friendly_summary_error()
    {
        var (handler, _, _) = NewHandler(_ => Csv("Widget,Amount\nFoo,10\n")); // no Name column → nothing parses

        var result = await handler.HandleAsync(
            new ParseGoogleSheetImportCommand(SheetUrl, null, Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(result.Rows);
        Assert.Contains(result.Report.RowErrors, e => e.Message.Contains("import template", StringComparison.OrdinalIgnoreCase));
    }

    // ── fetch guards ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Redirect_to_login_reports_not_link_shared()
    {
        var (handler, _, _) = NewHandler(req =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.Found);
            r.Headers.Location = new Uri("https://accounts.google.com/ServiceLogin?continue=x");
            return r;
        });

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            handler.HandleAsync(new ParseGoogleSheetImportCommand(SheetUrl, null, Guid.NewGuid()), CancellationToken.None));
        Assert.Contains("not link-shared", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Redirect_to_untrusted_host_is_blocked()
    {
        var (handler, _, _) = NewHandler(req =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.Found);
            r.Headers.Location = new Uri("https://evil.example.com/steal");
            return r;
        });

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            handler.HandleAsync(new ParseGoogleSheetImportCommand(SheetUrl, null, Guid.NewGuid()), CancellationToken.None));
        Assert.Contains("untrusted host", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Html_login_body_reports_not_link_shared()
    {
        var (handler, _, _) = NewHandler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<!DOCTYPE html><html><head><title>Sign in</title></head></html>",
                    Encoding.UTF8, "text/html"),
            };
            return r;
        });

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            handler.HandleAsync(new ParseGoogleSheetImportCommand(SheetUrl, null, Guid.NewGuid()), CancellationToken.None));
        Assert.Contains("not link-shared", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Html_body_mislabelled_as_csv_is_still_rejected_by_sniff()
    {
        // Content-Type says CSV but the payload is a login page — the byte sniff must still catch it.
        var (handler, _, _) = NewHandler(_ => Csv("  <html><body>Sign in to continue</body></html>"));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            handler.HandleAsync(new ParseGoogleSheetImportCommand(SheetUrl, null, Guid.NewGuid()), CancellationToken.None));
        Assert.Contains("not link-shared", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Oversized_response_is_rejected()
    {
        var big = new byte[(10 * 1024 * 1024) + 16];
        Array.Fill(big, (byte)'a');
        var (handler, _, _) = NewHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(big), // ByteArrayContent sets Content-Length → cap trips on the header
        });

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            handler.HandleAsync(new ParseGoogleSheetImportCommand(SheetUrl, null, Guid.NewGuid()), CancellationToken.None));
        Assert.Contains("10 MB", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Too_many_redirects_is_rejected()
    {
        // Always redirect within the allow-list → the hop budget must eventually trip.
        var (handler, _, _) = NewHandler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            r.Headers.Location = new Uri("https://doc-0-sheets.googleusercontent.com/pub?loop=1");
            return r;
        });

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            handler.HandleAsync(new ParseGoogleSheetImportCommand(SheetUrl, null, Guid.NewGuid()), CancellationToken.None));
        Assert.Contains("redirect", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── scaffolding ───────────────────────────────────────────────────────────────

    private static HttpResponseMessage Csv(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/csv") };

    private static (ParseGoogleSheetImportHandler Handler, LaundryGharDbContext Raw, Guid BrandId)
        NewHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brandId = Guid.NewGuid();
        var user = new ImportTestSupport.FakeCurrentUser(brandId);
        var parse = new ImportParseService(db, user, new FakeStorage());
        var factory = new FakeHttpClientFactory(new HttpClient(new ScriptedHandler(responder)));
        var handler = new ParseGoogleSheetImportHandler(factory, parse);
        return (handler, raw, brandId);
    }

    private static void SeedService(LaundryGharDbContext raw, Guid brandId, string name) =>
        raw.Services.Add(new Service
        {
            Id = Guid.NewGuid(), BrandId = brandId, CategoryId = Guid.NewGuid(),
            Code = name.ToUpperInvariant(), Name = name, NameLocalized = $"{{\"en\":\"{name}\"}}",
            PricingModel = "per_piece", BaseTatHours = 24, ExpressTatHours = 12, ExpressMultiplier = 1.5m,
            Status = "active", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, Version = 1,
        });

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(responder(request));
    }

    private sealed class FakeStorage : IFileStorageProvider
    {
        public Task<string> SaveAsync(Stream content, string contentType, string keyHint, Guid brandId, CancellationToken ct = default)
            => Task.FromResult($"{brandId:N}/{keyHint}/{Guid.NewGuid():N}.csv");
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream());
        public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public string? GetPublicUrl(string key) => null;
    }
}
