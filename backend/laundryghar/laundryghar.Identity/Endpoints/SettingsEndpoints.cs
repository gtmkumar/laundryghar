using laundryghar.Identity.Application.Settings.Commands;
using laundryghar.Identity.Application.Settings.Dtos;
using laundryghar.Identity.Application.Settings.Queries;
using laundryghar.Identity.Infrastructure.Services;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace laundryghar.Identity.Endpoints;

/// <summary>
/// /api/v1/admin/settings — platform/brand configuration (email transport,
/// user-provisioning mode, payment gateway credentials, etc.).
///
/// Authorization model (R3-SEC-3):
///   GET  /settings        → permission:settings.read   (brand_admin + platform_admin)
///   PUT/POST /settings/*  → permission:settings.manage (brand_admin + platform_admin)
///
/// Platform admins bypass permission checks entirely (handled by PermissionHandler).
/// The in-handler IsPlatformAdmin / UserType guard is kept as defence-in-depth:
/// it short-circuits before the command and avoids unnecessary handler execution,
/// but the permission policy is now the authoritative gate.
/// </summary>
public static class SettingsEndpoints
{
    private const string Read   = "permission:settings.read";
    private const string Manage = "permission:settings.manage";

    public static RouteGroupBuilder MapSettingsEndpoints(this RouteGroupBuilder group)
    {
        var s = group.MapGroup("/settings").WithTags("Admin - Settings").RequireAuthorization();

        s.MapGet("/", async (ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            if (Forbidden(u, out var deny)) return deny;
            var r = await sender.Send(new GetAdminSettingsQuery(u), ct);
            return Results.Ok(new SingleResponse<AdminSettingsView> { Status = true, Data = r });
        }).WithName("GetAdminSettings")
          .RequireAuthorization(Read);

        s.MapPut("/email", async (UpdateEmailSettingsRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            if (Forbidden(u, out var deny)) return deny;
            var r = await sender.Send(new UpdateEmailSettingsCommand(req, u), ct);
            return Results.Ok(new SingleResponse<EmailSettingsView> { Status = true, Data = r });
        }).WithName("UpdateEmailSettings")
          .RequireAuthorization(Manage);

        s.MapPost("/email/test", async (TestEmailRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            if (Forbidden(u, out var deny)) return deny;
            var r = await sender.Send(new TestEmailCommand(req, u), ct);
            return Results.Ok(new SingleResponse<TestEmailResult> { Status = true, Data = r });
        }).WithName("TestEmailSettings")
          .RequireAuthorization(Manage);

        s.MapPut("/provisioning", async (UpdateProvisioningRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            if (Forbidden(u, out var deny)) return deny;
            var r = await sender.Send(new UpdateProvisioningCommand(req, u), ct);
            return Results.Ok(new SingleResponse<ProvisioningView> { Status = true, Data = r });
        }).WithName("UpdateProvisioning")
          .RequireAuthorization(Manage);

        s.MapPut("/maps", async (UpdateMapsSettingsRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            if (Forbidden(u, out var deny)) return deny;
            var r = await sender.Send(new UpdateMapsCommand(req, u), ct);
            return Results.Ok(new SingleResponse<MapsSettingsView> { Status = true, Data = r });
        }).WithName("UpdateMapsSettings")
          .RequireAuthorization(Manage);

        s.MapPut("/payout", async (UpdatePayoutSettingsRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            if (Forbidden(u, out var deny)) return deny;
            var r = await sender.Send(new UpdatePayoutCommand(req, u), ct);
            return Results.Ok(new SingleResponse<PayoutSettingsView> { Status = true, Data = r });
        }).WithName("UpdatePayoutSettings")
          .RequireAuthorization(Manage);

        s.MapPut("/payment-gateway", async ([FromBody] UpdatePaymentGatewayRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            if (Forbidden(u, out var deny)) return deny;
            var r = await sender.Send(new UpdatePaymentGatewayCommand(req, u), ct);
            return Results.Ok(new SingleResponse<PaymentGatewaySettingsView> { Status = true, Data = r });
        }).WithName("UpdatePaymentGatewaySettings")
          .RequireAuthorization(Manage);

        s.MapPut("/whatsapp", async ([FromBody] UpdateWhatsAppRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            if (Forbidden(u, out var deny)) return deny;
            var r = await sender.Send(new UpdateWhatsAppCommand(req, u), ct);
            return Results.Ok(new SingleResponse<WhatsAppSettingsView> { Status = true, Data = r });
        }).WithName("UpdateWhatsAppSettings")
          .RequireAuthorization(Manage);

        s.MapPut("/sms", async ([FromBody] UpdateSmsRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            if (Forbidden(u, out var deny)) return deny;
            var r = await sender.Send(new UpdateSmsCommand(req, u), ct);
            return Results.Ok(new SingleResponse<SmsSettingsView> { Status = true, Data = r });
        }).WithName("UpdateSmsSettings")
          .RequireAuthorization(Manage);

        return group;
    }

    // Defence-in-depth: the permission policy is the authoritative gate.
    // This guard short-circuits before handler execution as an extra layer and
    // preserves the original brand-scoping logic (brand_admin only sees their brand).
    private static bool Forbidden(ICurrentUser u, out IResult result)
    {
        var allowed = u.IsPlatformAdmin || string.Equals(u.UserType, "brand_admin", StringComparison.OrdinalIgnoreCase);
        result = allowed ? Results.Empty : Results.Forbid();
        return !allowed;
    }
}
