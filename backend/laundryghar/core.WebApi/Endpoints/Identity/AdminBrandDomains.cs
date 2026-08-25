using core.Application.Identity.TenancyOrg.BrandDomains.Commands;
using core.Application.Identity.TenancyOrg.BrandDomains.Dtos;
using core.Application.Identity.TenancyOrg.BrandDomains.Queries;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

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

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Brand Domains").RequireAuthorization();

        group.MapGet(List, "").RequireAuthorization("permission:brands.read");
        group.MapPost(Add, "").RequireAuthorization("permission:brands.update");
        group.MapPost(Verify, "{domainId:guid}/verify").RequireAuthorization("permission:brands.update");
        group.MapDelete(Delete, "{domainId:guid}").RequireAuthorization("permission:brands.update");
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
}
