using core.Application.Identity.Impersonation.Commands;
using core.Application.Identity.Impersonation.Dtos;
using core.Application.Identity.Impersonation.Queries;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// Support impersonation with consent (§7, §8.1). Two sides, two permissions, deliberately held by
/// disjoint roles (asserted in migration 0014):
///
/// <list type="bullet">
/// <item><b>Support</b> (<c>impersonation.request</c>) may ask, poll the answer, and start a session
/// once approved. Nothing here reads tenant data before consent exists.</item>
/// <item><b>The provider</b> (<c>impersonation.approve</c>, CRITICAL → step-up required) decides,
/// and can end a live session at any time.</item>
/// </list>
///
/// <para>This does not replace §2.3 "operate-as-tenant" for <c>platform_admin</c>, which is
/// unchanged. It is the path for support, who hold no RLS bypass.</para>
/// </summary>
public class AdminImpersonation : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/impersonation";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Impersonation").RequireAuthorization();

        // ── the support side ───────────────────────────────────────────────────────────────────
        group.MapPost(Request, "requests").RequireAuthorization("permission:impersonation.request");
        group.MapGet(GetState, "requests/{grantId:guid}/state").RequireAuthorization("permission:impersonation.request");
        group.MapPost(Start, "requests/{grantId:guid}/start").RequireAuthorization("permission:impersonation.request");

        // ── the provider side ──────────────────────────────────────────────────────────────────
        group.MapGet(List, "grants").RequireAuthorization("permission:impersonation.approve");
        group.MapPost(Decide, "grants/{grantId:guid}/decision").RequireAuthorization("permission:impersonation.approve");
        group.MapPost(Revoke, "grants/{grantId:guid}/revoke").RequireAuthorization("permission:impersonation.approve");
    }

    public static async Task<IResult> Request(
        RequestImpersonationRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var id = await dispatcher.SendAsync(
            new RequestImpersonationCommand(req, user.UserId ?? Guid.Empty), ct);
        return id is null ? Results.NotFound() : Results.Ok(new SingleResponse<Guid> { Status = true, Data = id.Value });
    }

    /// <summary>
    /// Lets support see whether they have been let in yet. Answers from the SECURITY DEFINER oracle,
    /// so it works before any consent exists — and returns only status/scope/expiry, never a byte of
    /// the provider's data.
    /// </summary>
    public static async Task<IResult> GetState(
        Guid grantId, IImpersonationStateStore store, CancellationToken ct)
    {
        var state = await store.GetAsync(grantId, ct);
        return state is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<ImpersonationStateDto>
            {
                Status = true,
                Data = new ImpersonationStateDto(grantId, state.Status, state.Scope, state.ExpiresAt),
            });
    }

    public static async Task<IResult> Start(
        Guid grantId, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var session = await dispatcher.SendAsync(
            new StartImpersonationSessionCommand(grantId, user.UserId ?? Guid.Empty), ct);
        return Results.Ok(new SingleResponse<ImpersonationSessionDto> { Status = true, Data = session });
    }

    public static async Task<IResult> List(
        Guid? brandId, IDispatcher dispatcher, CancellationToken ct)
    {
        var rows = await dispatcher.QueryAsync(new GetImpersonationGrantsQuery(brandId), ct);
        return Results.Ok(new SingleResponse<IReadOnlyList<ImpersonationGrantDto>> { Status = true, Data = rows });
    }

    public static async Task<IResult> Decide(
        Guid grantId, DecideImpersonationRequest req, ICurrentUser user,
        IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DecideImpersonationCommand(grantId, req, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    public static async Task<IResult> Revoke(
        Guid grantId, RevokeImpersonationRequest req, ICurrentUser user,
        IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new RevokeImpersonationCommand(grantId, req, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
