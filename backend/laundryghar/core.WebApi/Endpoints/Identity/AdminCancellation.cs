using System.Text;
using core.Application.Identity.Cancellation.Commands;
using core.Application.Identity.Cancellation.Dtos;
using core.Application.Identity.Cancellation.Queries;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// §9 "Cancellation = export offered, wind-down retention, then deletion per DPDP" and §8.2 "export
/// their data at any time; take it with them if they leave."
///
/// <para>Reachable while the brand is <c>cancelled</c> — this prefix is on
/// <c>BrandSuspensionMiddleware</c>'s allow-list, because a retention window the customer cannot
/// export from is deletion with a delay.</para>
///
/// <para>Note what is absent: there is no delete endpoint. The purge belongs to the worker, on the
/// clock the retention window sets. A request path that destroys a tenant is one mis-click from
/// unrecoverable, and nothing about §9 asks for one.</para>
/// </summary>
public class AdminCancellation : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/cancellation";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Cancellation").RequireAuthorization();

        group.MapGet(Get, "").RequireAuthorization("permission:settings.read");
        group.MapGet(Export, "export").RequireAuthorization("permission:settings.manage");
        group.MapPost(Request, "").RequireAuthorization("permission:settings.manage");
        group.MapPost(Withdraw, "withdraw").RequireAuthorization("permission:settings.manage");
    }

    public static async Task<IResult> Get(ICurrentTenant tenant, IDispatcher dispatcher, CancellationToken ct)
    {
        var brandId = tenant.BrandId;
        if (brandId is null) return Results.BadRequest();

        var dto = await dispatcher.QueryAsync(new GetBrandCancellationQuery(brandId.Value), ct);
        return dto is null
            ? Results.NoContent()
            : Results.Ok(new SingleResponse<BrandCancellationDto> { Status = true, Data = dto });
    }

    /// <summary>
    /// Streams the whole tenant as newline-delimited JSON — one <c>{"source":…,"data":…}</c> per
    /// record.
    ///
    /// <para>NDJSON rather than one JSON document because the consumer can start processing at the
    /// first line and neither side ever holds the whole thing. A 200 MB array that must be complete
    /// before it is valid is the format most likely to fail for exactly the customers who need it
    /// most.</para>
    ///
    /// <para>The brand id comes from the CALLER'S OWN TENANT, never from the route or a query
    /// parameter. <c>kernel.export_brand</c> is SECURITY DEFINER and trusts its argument, so letting
    /// a client name the brand would turn this endpoint into a read of any tenant on the
    /// platform.</para>
    /// </summary>
    public static async Task Export(
        HttpContext http, ICurrentTenant tenant, ICurrentUser user,
        IBrandExportService exporter, IDispatcher dispatcher, CancellationToken ct)
    {
        var brandId = tenant.BrandId;
        if (brandId is null)
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        http.Response.ContentType = "application/x-ndjson";
        http.Response.Headers.ContentDisposition =
            $"attachment; filename=\"laundryghar-export-{brandId:N}.ndjson\"";

        // UTF8Encoding(false), NOT Encoding.UTF8: the latter emits a byte-order mark, and a BOM at
        // the head of an NDJSON stream makes the FIRST line unparseable to strict JSON readers —
        // including Python's json.loads. Caught by exporting a real tenant and trying to read it
        // back, which is the only way this kind of defect ever shows up.
        await using var writer = new StreamWriter(
            http.Response.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
        var count = 0L;
        await foreach (var record in exporter.StreamAsync(brandId.Value, ct))
        {
            await writer.WriteLineAsync($"{{\"source\":\"{record.Source}\",\"data\":{record.Json}}}");
            // Flush periodically so a slow, large export does not sit invisible behind a buffer.
            if (++count % 500 == 0) await writer.FlushAsync(ct);
        }
        await writer.FlushAsync(ct);

        // Recorded AFTER the stream completes, so the count means "exports that finished", which is
        // the only version of it worth showing an owner deciding whether they have their data.
        await dispatcher.SendAsync(new RecordBrandExportCommand(brandId.Value, count), CancellationToken.None);
    }

    public static async Task<IResult> Request(
        RequestBrandCancellationRequest req, ICurrentTenant tenant, ICurrentUser user,
        IDispatcher dispatcher, CancellationToken ct)
    {
        var brandId = tenant.BrandId;
        if (brandId is null) return Results.BadRequest();

        var id = await dispatcher.SendAsync(
            new RequestBrandCancellationCommand(brandId.Value, req, user.UserId), ct);
        return id is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<Guid> { Status = true, Data = id.Value });
    }

    public static async Task<IResult> Withdraw(
        WithdrawBrandCancellationRequest req, ICurrentTenant tenant, ICurrentUser user,
        IDispatcher dispatcher, CancellationToken ct)
    {
        var brandId = tenant.BrandId;
        if (brandId is null) return Results.BadRequest();

        var ok = await dispatcher.SendAsync(
            new WithdrawBrandCancellationCommand(brandId.Value, req, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
