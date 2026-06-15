using core.Application.Identity.Settings.Commands.TestEmail;
using core.Application.Identity.Settings.Commands.UpdateDispatchSettings;
using core.Application.Identity.Settings.Commands.UpdateEmailSettings;
using core.Application.Identity.Settings.Commands.UpdateFareSettings;
using core.Application.Identity.Settings.Commands.UpdateMaps;
using core.Application.Identity.Settings.Commands.UpdatePaymentGateway;
using core.Application.Identity.Settings.Commands.UpdatePayout;
using core.Application.Identity.Settings.Commands.UpdateProvisioning;
using core.Application.Identity.Settings.Commands.UpdateSms;
using core.Application.Identity.Settings.Commands.UpdateWhatsApp;
using core.Application.Identity.Settings.Dtos;
using core.Application.Identity.Settings.Queries.GetAdminSettings;
using core.Application.Identity.Settings.Queries.GetDispatchSettings;
using core.Application.Identity.Settings.Queries.GetFareSettings;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Common;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using Microsoft.AspNetCore.Mvc;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// /api/v1/admin/settings — platform/brand configuration (email transport,
/// user-provisioning mode, payment gateway credentials, etc.).
///
/// Authorization model (R3-SEC-3):
///   GET  /settings        → permission:settings.read   (brand_admin + platform_admin)
///   PUT/POST /settings/*  → permission:settings.manage (brand_admin + platform_admin)
///
/// Platform admins bypass permission checks entirely (handled by the permission policy).
/// The in-handler IsPlatformAdmin / UserType guard is kept as defence-in-depth:
/// it short-circuits before the command and avoids unnecessary handler execution,
/// but the permission policy is now the authoritative gate.
/// </summary>
public class AdminSettings : IEndpointGroup
{
    private const string Read   = "permission:settings.read";
    private const string Manage = "permission:settings.manage";

    public static string? RoutePrefix => "/api/v1/admin/settings";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Settings").RequireAuthorization();

        group.MapGet(GetAdminSettings).RequireAuthorization(Read);
        group.MapPut(UpdateEmailSettings, "email").RequireAuthorization(Manage);
        group.MapPost(TestEmailSettings, "email/test").RequireAuthorization(Manage);
        group.MapPut(UpdateProvisioning, "provisioning").RequireAuthorization(Manage);
        group.MapPut(UpdateMapsSettings, "maps").RequireAuthorization(Manage);
        group.MapPut(UpdatePayoutSettings, "payout").RequireAuthorization(Manage);
        group.MapPut(UpdatePaymentGatewaySettings, "payment-gateway").RequireAuthorization(Manage);

        // ── Marketplace: fare (brand) + dispatch (platform) ───────────────────
        group.MapGet(GetFareSettings, "fare").RequireAuthorization(Read);
        group.MapPut(UpdateFareSettings, "fare").RequireAuthorization(Manage);
        group.MapGet(GetDispatchSettings, "dispatch").RequireAuthorization(Read);
        group.MapPut(UpdateDispatchSettings, "dispatch").RequireAuthorization(Manage);
        group.MapPut(UpdateWhatsAppSettings, "whatsapp").RequireAuthorization(Manage);
        group.MapPut(UpdateSmsSettings, "sms").RequireAuthorization(Manage);
    }

    public static async Task<IResult> GetAdminSettings(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (Forbidden(u, out var deny)) return deny;
        var r = await dispatcher.QueryAsync(new GetAdminSettingsQuery(), ct);
        return Results.Ok(new SingleResponse<AdminSettingsView> { Status = true, Data = r });
    }

    public static async Task<IResult> UpdateEmailSettings(UpdateEmailSettingsRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (Forbidden(u, out var deny)) return deny;
        var r = await dispatcher.SendAsync(new UpdateEmailSettingsCommand(req), ct);
        return Results.Ok(new SingleResponse<EmailSettingsView> { Status = true, Data = r });
    }

    public static async Task<IResult> TestEmailSettings(TestEmailRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (Forbidden(u, out var deny)) return deny;
        var r = await dispatcher.SendAsync(new TestEmailCommand(req), ct);
        return Results.Ok(new SingleResponse<TestEmailResult> { Status = true, Data = r });
    }

    public static async Task<IResult> UpdateProvisioning(UpdateProvisioningRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (Forbidden(u, out var deny)) return deny;
        var r = await dispatcher.SendAsync(new UpdateProvisioningCommand(req), ct);
        return Results.Ok(new SingleResponse<ProvisioningView> { Status = true, Data = r });
    }

    public static async Task<IResult> UpdateMapsSettings(UpdateMapsSettingsRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (Forbidden(u, out var deny)) return deny;
        var r = await dispatcher.SendAsync(new UpdateMapsCommand(req), ct);
        return Results.Ok(new SingleResponse<MapsSettingsView> { Status = true, Data = r });
    }

    public static async Task<IResult> UpdatePayoutSettings(UpdatePayoutSettingsRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (Forbidden(u, out var deny)) return deny;
        var r = await dispatcher.SendAsync(new UpdatePayoutCommand(req), ct);
        return Results.Ok(new SingleResponse<PayoutSettingsView> { Status = true, Data = r });
    }

    public static async Task<IResult> UpdatePaymentGatewaySettings([FromBody] UpdatePaymentGatewayRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (Forbidden(u, out var deny)) return deny;
        var r = await dispatcher.SendAsync(new UpdatePaymentGatewayCommand(req), ct);
        return Results.Ok(new SingleResponse<PaymentGatewaySettingsView> { Status = true, Data = r });
    }

    public static async Task<IResult> GetFareSettings(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (Forbidden(u, out var deny)) return deny;
        var r = await dispatcher.QueryAsync(new GetFareSettingsQuery(), ct);
        return Results.Ok(new SingleResponse<FareSettings> { Status = true, Data = r });
    }

    public static async Task<IResult> UpdateFareSettings([FromBody] FareSettings req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (Forbidden(u, out var deny)) return deny;
        var r = await dispatcher.SendAsync(new UpdateFareSettingsCommand(req), ct);
        return Results.Ok(new SingleResponse<FareSettings> { Status = true, Data = r });
    }

    public static async Task<IResult> GetDispatchSettings(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (Forbidden(u, out var deny)) return deny;
        var r = await dispatcher.QueryAsync(new GetDispatchSettingsQuery(), ct);
        return Results.Ok(new SingleResponse<DispatchSettings> { Status = true, Data = r });
    }

    public static async Task<IResult> UpdateDispatchSettings([FromBody] DispatchSettings req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (Forbidden(u, out var deny)) return deny;
        var r = await dispatcher.SendAsync(new UpdateDispatchSettingsCommand(req), ct);
        return Results.Ok(new SingleResponse<DispatchSettings> { Status = true, Data = r });
    }

    public static async Task<IResult> UpdateWhatsAppSettings([FromBody] UpdateWhatsAppRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (Forbidden(u, out var deny)) return deny;
        var r = await dispatcher.SendAsync(new UpdateWhatsAppCommand(req), ct);
        return Results.Ok(new SingleResponse<WhatsAppSettingsView> { Status = true, Data = r });
    }

    public static async Task<IResult> UpdateSmsSettings([FromBody] UpdateSmsRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (Forbidden(u, out var deny)) return deny;
        var r = await dispatcher.SendAsync(new UpdateSmsCommand(req), ct);
        return Results.Ok(new SingleResponse<SmsSettingsView> { Status = true, Data = r });
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
