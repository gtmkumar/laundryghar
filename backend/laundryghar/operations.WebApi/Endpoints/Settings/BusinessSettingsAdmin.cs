using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Caching;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Settings.Commands.DeleteSetting;
using operations.Application.Settings.Commands.UpsertSetting;
using operations.Application.Settings.Dtos;
using operations.Application.Settings.Queries.ListSettings;
using operations.WebApi.Endpoints.Catalog; // CatalogCacheTags

namespace operations.WebApi.Endpoints.Settings;

/// <summary>
/// Admin — scope-aware business-rule settings (tax, TAT, currency, order/catalog/logistics rules)
/// stored in kernel.system_settings with store→franchise→brand→platform precedence.
///
/// Distinct from the core host's /api/v1/admin/settings (email/maps/gateway bundle): this surface
/// is a generic scoped key-value CRUD whose consumers live in the operations bounded context.
///
/// Authorization:
///   GET    → permission:settings.read   (brand_admin + platform_admin)
///   PUT    → permission:settings.manage
///   DELETE → permission:settings.manage
/// A franchise/store-scoped caller may only read/write within its own subtree (enforced in-handler
/// via ICurrentUser.IsWithinScope, complementing per-brand RLS).
/// </summary>
public class BusinessSettingsAdmin : IEndpointGroup
{
    private const string Read   = "permission:settings.read";
    private const string Manage = "permission:settings.manage";

    public static string? RoutePrefix => "/api/v1/admin/business-settings";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Business Settings")
             // Settings upsert/delete change the scope-resolved values behind the customer
             // catalog config read (min order value, currency, high-value garment threshold).
             .EvictOutputCacheOnWrite(CatalogCacheTags.Config);

        group.MapGet(List, "/").RequireAuthorization(Read);
        group.MapPut(Upsert, "/")
            .AddEndpointFilter<ValidationFilter<UpsertSettingRequest>>()
            .RequireAuthorization(Manage);
        group.MapDelete(Delete, "/").RequireAuthorization(Manage);
    }

    /// <summary>GET /?category=&amp;franchiseId=&amp;storeId= → raw rows + effective value per key.</summary>
    public static async Task<IResult> List(
        string category, IDispatcher dispatcher, CancellationToken ct,
        Guid? franchiseId = null, Guid? storeId = null)
    {
        if (string.IsNullOrWhiteSpace(category))
            return Results.BadRequest(new Response
            {
                Status = false,
                Message = new() { ResponseMessage = "category is required." }
            });

        var r = await dispatcher.QueryAsync(new ListSettingsQuery(category, franchiseId, storeId), ct);
        return Results.Ok(new SingleResponse<SettingsListDto> { Status = true, Data = r });
    }

    /// <summary>PUT / — upsert (or clear when value is null) a scope's row for a key.</summary>
    public static async Task<IResult> Upsert(
        UpsertSettingRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpsertSettingCommand(req, u.UserId), ct);
        return r is null
            ? Results.Ok(new Response { Status = true, Message = new() { ResponseMessage = "Setting cleared." } })
            : Results.Ok(new SingleResponse<SettingRowDto> { Status = true, Data = r });
    }

    /// <summary>DELETE /?category=&amp;key=&amp;scopeType=&amp;franchiseId=&amp;storeId= — clear a scope's row.</summary>
    public static async Task<IResult> Delete(
        string category, string key, string scopeType,
        IDispatcher dispatcher, CancellationToken ct,
        Guid? franchiseId = null, Guid? storeId = null)
    {
        var ok = await dispatcher.SendAsync(
            new DeleteSettingCommand(category, key, scopeType, franchiseId, storeId), ct);
        return ok
            ? Results.Ok(new Response { Status = true })
            : Results.NotFound(new Response { Status = false, Message = new() { ResponseMessage = "No setting row at that scope." } });
    }
}
