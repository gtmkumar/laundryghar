using core.Application.Identity.ApiKeys.Commands;
using core.Application.Identity.ApiKeys.Dtos;
using core.Application.Identity.ApiKeys.Queries;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// A provider's machine credentials (§11 P4 + the <c>api_access</c> entitlement in §5).
///
/// <para>Gated on <c>api_keys.manage</c>, a CRITICAL permission — issuing a key mints a credential
/// to a whole business, so it demands a fresh step-up (§8) and is owner-side only.</para>
///
/// <para>The brand always comes from the caller's own tenant, never the route. A key-management
/// endpoint that accepts a brand id is one authorization bug away from issuing credentials to
/// someone else's company.</para>
/// </summary>
public class AdminApiKeys : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/api-keys";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - API keys").RequireAuthorization();

        group.MapGet(List, "").RequireAuthorization("permission:api_keys.manage");
        group.MapPost(Create, "").RequireAuthorization("permission:api_keys.manage");
        group.MapPost(Revoke, "{keyId:guid}/revoke").RequireAuthorization("permission:api_keys.manage");
    }

    public static async Task<IResult> List(ICurrentTenant tenant, IDispatcher dispatcher, CancellationToken ct)
    {
        if (tenant.BrandId is not { } brandId) return Results.BadRequest();

        var rows = await dispatcher.QueryAsync(new GetApiKeysQuery(brandId), ct);
        return Results.Ok(new SingleResponse<IReadOnlyList<ApiKeyDto>> { Status = true, Data = rows });
    }

    /// <summary>Returns the secret. Once. See <see cref="CreatedApiKeyDto"/>.</summary>
    public static async Task<IResult> Create(
        CreateApiKeyRequest req, ICurrentTenant tenant, ICurrentUser user,
        IDispatcher dispatcher, CancellationToken ct)
    {
        if (tenant.BrandId is not { } brandId) return Results.BadRequest();

        var created = await dispatcher.SendAsync(new CreateApiKeyCommand(brandId, req, user.UserId), ct);
        return Results.Ok(new SingleResponse<CreatedApiKeyDto> { Status = true, Data = created });
    }

    public static async Task<IResult> Revoke(
        Guid keyId, ICurrentTenant tenant, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        if (tenant.BrandId is not { } brandId) return Results.BadRequest();

        var ok = await dispatcher.SendAsync(new RevokeApiKeyCommand(brandId, keyId, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
