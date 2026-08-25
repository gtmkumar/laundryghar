using core.Application.Identity.TenancyOrg.BrandDomains.Commands;
using core.Application.Identity.TenancyOrg.BrandDomains.Dtos;
using core.Application.Identity.TenancyOrg.BrandDomains.Queries;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

using Microsoft.EntityFrameworkCore;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// Admin — custom domains for white-label tier T2 (PLATFORM_STRATEGY.md §4.2):
/// <c>/api/v1/admin/brands/{brandId}/domains</c>.
///
/// <para>Gated on <c>brands.read</c> / <c>brands.update</c> rather than the platform-side
/// <c>saas.*</c>: a domain is the provider's own branding, and §6 Law 1 puts branding, domain and
/// subscription with the Owner. <c>brands.update</c> carries risk level <b>high</b>, so the existing
/// step-up guard (docs/rbac.md §8) applies to every mutation here for free — appropriate, since
/// verifying a domain is what makes a hostname start serving a tenant's traffic.</para>
/// </summary>
public class AdminBrandDomains : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/brands/{brandId:guid}/domains";

    // Gated on `domains.*`, NOT `brands.*`. Those permissions cover the whole brand-settings surface
    // — name, logo, support details — and re-pointing them at the `custom_domain` feature would make
    // a brand's own NAME uneditable unless they bought a domain add-on. Migration 0019 grants the new
    // permissions to exactly the roles that already held brands.update, so nobody gains reach; what
    // changes is that these routes now sit behind the feature §5 sells them as.
    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Brand Domains").RequireAuthorization();

        group.MapGet(List, "").RequireAuthorization("permission:domains.read");

        // §12's "automated SSL/domain health checks from day one" — the platform's own view of which
        // custom domains are about to break. Platform-scoped, so it is gated on saas.read rather than
        // a tenant permission: a provider sees their own domains through the panel above.
        group.MapGet(GetNeedingAttention, "/health").RequireAuthorization("permission:saas.read");
        group.MapPost(Add, "").RequireAuthorization("permission:domains.manage");
        group.MapPost(Verify, "{domainId:guid}/verify").RequireAuthorization("permission:domains.manage");
        group.MapDelete(Delete, "{domainId:guid}").RequireAuthorization("permission:domains.manage");
    }

    public static async Task<IResult> List(Guid brandId, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetBrandDomainsQuery(brandId), ct);
        return Results.Ok(new SingleResponse<IReadOnlyList<BrandDomainDto>> { Status = true, Data = data });
    }

    public static async Task<IResult> Add(
        Guid brandId, AddBrandDomainRequest req,
        ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new AddBrandDomainCommand(brandId, req, user.UserId), ct);
        return Results.Ok(new SingleResponse<BrandDomainDto> { Status = true, Data = data });
    }

    /// <summary>
    /// Runs the DNS check. Always 200 on a found row: "not verified yet" is an ordinary,
    /// expected outcome of this flow (DNS propagates slowly), not a client error — the caller reads
    /// <c>Verified</c> / <c>Status</c> to know what happened. 404 means the domain row itself is not
    /// this brand's.
    /// </summary>
    public static async Task<IResult> Verify(
        Guid brandId, Guid domainId,
        ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new VerifyBrandDomainCommand(brandId, domainId, user.UserId), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<VerifyBrandDomainResultDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Delete(
        Guid brandId, Guid domainId, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteBrandDomainCommand(brandId, domainId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    /// <summary>
    /// Domains that need a human: unreachable, degraded, failing repeatedly, expiring within the
    /// window, or simply not checked lately — because silence is not health.
    /// </summary>
    public static async Task<IResult> GetNeedingAttention(
        int? withinDays,
        laundryghar.SharedDataModel.Persistence.LaundryGharDbContext db,
        CancellationToken ct)
    {
        var days = withinDays is > 0 and <= 365 ? withinDays.Value : 21;

        var rows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.Database.SqlQuery<DomainHealthRow>($"""
                SELECT domain               AS "Domain",
                       brand_code           AS "BrandCode",
                       health_status        AS "HealthStatus",
                       ssl_status           AS "SslStatus",
                       ssl_expires_at       AS "SslExpiresAt",
                       days_left            AS "DaysLeft",
                       consecutive_failures AS "ConsecutiveFailures",
                       last_checked_at      AS "LastCheckedAt"
                FROM kernel.domains_needing_attention({days})
                """), ct);

        return Results.Ok(new SingleResponse<IReadOnlyList<DomainHealthRow>> { Status = true, Data = rows });
    }

    /// <param name="DaysLeft">Null when no certificate has ever been observed.</param>
    public sealed record DomainHealthRow(
        string Domain, string? BrandCode, string HealthStatus, string SslStatus,
        DateTimeOffset? SslExpiresAt, int? DaysLeft, int ConsecutiveFailures,
        DateTimeOffset? LastCheckedAt);
}
