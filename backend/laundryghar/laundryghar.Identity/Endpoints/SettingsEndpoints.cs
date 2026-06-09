using laundryghar.Identity.Application.Settings.Commands;
using laundryghar.Identity.Application.Settings.Dtos;
using laundryghar.Identity.Application.Settings.Queries;
using laundryghar.Identity.Infrastructure.Services;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using MediatR;

namespace laundryghar.Identity.Endpoints;

/// <summary>
/// /api/v1/admin/settings — platform/brand configuration (email transport,
/// user-provisioning mode). Restricted to platform or brand administrators.
/// </summary>
public static class SettingsEndpoints
{
    public static RouteGroupBuilder MapSettingsEndpoints(this RouteGroupBuilder group)
    {
        var s = group.MapGroup("/settings").WithTags("Admin - Settings").RequireAuthorization();

        s.MapGet("/", async (ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            if (Forbidden(u, out var deny)) return deny;
            var r = await sender.Send(new GetAdminSettingsQuery(u), ct);
            return Results.Ok(new SingleResponse<AdminSettingsView> { Status = true, Data = r });
        }).WithName("GetAdminSettings");

        s.MapPut("/email", async (UpdateEmailSettingsRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            if (Forbidden(u, out var deny)) return deny;
            var r = await sender.Send(new UpdateEmailSettingsCommand(req, u), ct);
            return Results.Ok(new SingleResponse<EmailSettingsView> { Status = true, Data = r });
        }).WithName("UpdateEmailSettings");

        s.MapPost("/email/test", async (TestEmailRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            if (Forbidden(u, out var deny)) return deny;
            var r = await sender.Send(new TestEmailCommand(req, u), ct);
            return Results.Ok(new SingleResponse<TestEmailResult> { Status = true, Data = r });
        }).WithName("TestEmailSettings");

        s.MapPut("/provisioning", async (UpdateProvisioningRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            if (Forbidden(u, out var deny)) return deny;
            var r = await sender.Send(new UpdateProvisioningCommand(req, u), ct);
            return Results.Ok(new SingleResponse<ProvisioningView> { Status = true, Data = r });
        }).WithName("UpdateProvisioning");

        s.MapPut("/maps", async (UpdateMapsSettingsRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            if (Forbidden(u, out var deny)) return deny;
            var r = await sender.Send(new UpdateMapsCommand(req, u), ct);
            return Results.Ok(new SingleResponse<MapsSettingsView> { Status = true, Data = r });
        }).WithName("UpdateMapsSettings");

        s.MapPut("/payout", async (UpdatePayoutSettingsRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            if (Forbidden(u, out var deny)) return deny;
            var r = await sender.Send(new UpdatePayoutCommand(req, u), ct);
            return Results.Ok(new SingleResponse<PayoutSettingsView> { Status = true, Data = r });
        }).WithName("UpdatePayoutSettings");

        return group;
    }

    // Settings are admin-only: platform admins, or brand admins for their own brand.
    private static bool Forbidden(ICurrentUser u, out IResult result)
    {
        var allowed = u.IsPlatformAdmin || string.Equals(u.UserType, "brand_admin", StringComparison.OrdinalIgnoreCase);
        result = allowed ? Results.Empty : Results.Forbid();
        return !allowed;
    }
}
