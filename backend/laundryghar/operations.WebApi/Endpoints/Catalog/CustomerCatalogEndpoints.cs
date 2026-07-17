using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Caching;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using Microsoft.AspNetCore.Mvc;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Catalog.Queries.Service;
using operations.Application.Catalog.Catalog.Queries.ServiceCategory;
using operations.Application.Catalog.Customer.Self.Commands;
using operations.Application.Catalog.Customer.Self.Dtos;
using operations.Application.Catalog.Customer.Self.Queries;
using operations.Application.Catalog.Pricing.Dtos;
using operations.Application.Catalog.Pricing.Queries.PriceResolution;

namespace operations.WebApi.Endpoints.Catalog;

/// <summary>
/// Output-cache tags for the customer-facing catalog reads. Each tag couples a cached
/// customer read to the admin group(s) that edit the underlying content
/// (<c>EvictOutputCacheOnWrite</c>) so changes regenerate the cached response immediately;
/// the per-endpoint TTL is only a backstop.
/// </summary>
public static class CatalogCacheTags
{
    public const string Categories = "catalog:categories";
    public const string Services   = "catalog:services";
    public const string PriceList  = "catalog:price-list";
    public const string Config     = "catalog:config";
}

/// <summary>
/// Customer-facing catalog + self-service lane (/api/v1/customer/*). Group-gated by the
/// "CustomerOnly" policy (token_use=customer). All self-service routes are self-filtered:
/// the customer id is derived from the JWT sub (ICurrentUser.UserId) and brand_id claim
/// (ICurrentUser.BrandId) — never from the request body.
/// </summary>
public class CustomerCatalogEndpoints : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/customer";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Customer - Catalog").RequireAuthorization("CustomerOnly");

        // ── Catalog reads (read-only, no pagination needed for customers) ──────
        // Output-cached: each response depends only on the caller's brand (folded into the
        // tenant cache key) plus the declared query params below — nothing per-user. Evicted
        // by the matching admin groups on write; TTLs are regenerate-on-schedule backstops.
        group.MapGet(GetCategories, "/catalog/categories")
            .CacheSharedOutput(CatalogCacheTags.Categories, TimeSpan.FromMinutes(10));
        group.MapGet(GetServices, "/catalog/services")
            .CacheSharedOutput(CatalogCacheTags.Services, TimeSpan.FromMinutes(10), "categoryId");
        group.MapGet(GetPriceList, "/catalog/price-list")
            .CacheSharedOutput(CatalogCacheTags.PriceList, TimeSpan.FromMinutes(10));
        group.MapGet(GetCatalogConfig, "/catalog/config")
            .CacheSharedOutput(CatalogCacheTags.Config, TimeSpan.FromMinutes(10), "storeId");

        // ── Profile ────────────────────────────────────────────────────────────
        group.MapGet(GetProfile, "/profile");
        group.MapPatch(PatchProfile, "/profile");

        // ── Addresses ──────────────────────────────────────────────────────────
        group.MapGet(GetAddresses, "/addresses");
        group.MapPost(CreateAddress, "/addresses").AddEndpointFilter<ValidationFilter<CreateAddressRequest>>();
        group.MapPut(UpdateAddress, "/addresses/{id:guid}").AddEndpointFilter<ValidationFilter<UpdateAddressRequest>>();
        group.MapDelete(DeleteAddress, "/addresses/{id:guid}");

        // ── Serviceability ───────────────────────────────────────────────────────
        group.MapGet(CheckServiceability, "/serviceability");

        // ── Devices ──────────────────────────────────────────────────────────────
        group.MapPost(RegisterDevice, "/devices").AddEndpointFilter<ValidationFilter<RegisterDeviceRequest>>();

        // ── DPDP Consents ──────────────────────────────────────────────────────
        group.MapGet(GetConsents, "/consents");
        group.MapPost(GrantConsent, "/consents/grant");
        group.MapPost(WithdrawConsent, "/consents/withdraw");

        // ── Push Tokens ──────────────────────────────────────────────────────────
        group.MapPost(RegisterPushToken, "/push-token");
        group.MapDelete(DeactivatePushToken, "/push-token");

        // ── Account Deletion ───────────────────────────────────────────────────
        group.MapPost(CreateDeletionRequest, "/account/deletion-request")
            .AddEndpointFilter<ValidationFilter<CreateDeletionRequestRequest>>();
        group.MapGet(GetDeletionRequest, "/account/deletion-request");
        group.MapDelete(CancelDeletionRequest, "/account/deletion-request");
    }

    // ── Catalog reads ───────────────────────────────────────────────────────────

    public static async Task<IResult> GetCategories(IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetServiceCategoriesQuery(1, 100), ct);
        return Results.Ok(new ListResponse<ServiceCategoryDto> { Status = true, Data = r.List.ToList() });
    }

    public static async Task<IResult> GetServices(Guid? categoryId, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetServicesQuery(1, 100, categoryId), ct);
        return Results.Ok(new ListResponse<ServiceDto> { Status = true, Data = r.List.ToList() });
    }

    public static async Task<IResult> GetPriceList(IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetPublishedPriceListQuery(), ct);
        return Results.Ok(new ListResponse<PriceListItemDto> { Status = true, Data = r.ToList() });
    }

    // GET /api/v1/customer/catalog/config?storeId=<guid?>
    // Scope-resolved order/catalog rules the app needs before building a cart: minimum order
    // value, currency, and the high-value garment threshold. Brand comes from the JWT; store is
    // optional (its franchise is derived server-side). Null minimum/threshold ⇒ no restriction.
    public static async Task<IResult> GetCatalogConfig(Guid? storeId, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var brandId = u.BrandId ?? Guid.Empty;
        if (brandId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetCustomerCatalogConfigQuery(brandId, storeId), ct);
        return Results.Ok(new SingleResponse<CustomerCatalogConfigDto> { Status = true, Data = r });
    }

    // ── Profile ───────────────────────────────────────────────────────────────

    public static async Task<IResult> GetProfile(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetMyProfileQuery(customerId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<CustomerProfileDto> { Status = true, Data = r });
    }

    public static async Task<IResult> PatchProfile(PatchProfileRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.SendAsync(new PatchMyProfileCommand(customerId, req), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<CustomerProfileDto> { Status = true, Data = r });
    }

    // ── Addresses ───────────────────────────────────────────────────────────────

    public static async Task<IResult> GetAddresses(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetMyAddressesQuery(customerId), ct);
        return Results.Ok(new ListResponse<CustomerAddressDto> { Status = true, Data = r.ToList() });
    }

    public static async Task<IResult> CreateAddress(CreateAddressRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        var brandId    = u.BrandId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.SendAsync(new CreateMyAddressCommand(customerId, brandId, req), ct);
        return Results.Created($"/api/v1/customer/addresses/{r.Id}",
            new SingleResponse<CustomerAddressDto> { Status = true, Data = r });
    }

    public static async Task<IResult> UpdateAddress(Guid id, UpdateAddressRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.SendAsync(new UpdateMyAddressCommand(customerId, id, req), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<CustomerAddressDto> { Status = true, Data = r });
    }

    public static async Task<IResult> DeleteAddress(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var ok = await dispatcher.SendAsync(new DeleteMyAddressCommand(customerId, id), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    // ── Serviceability ────────────────────────────────────────────────────────
    // GET /api/v1/customer/serviceability?pincode=110001
    // Returns { serviceable: true } when any active store in this brand covers the pincode,
    // OR the pincode is listed in at least one active territory. 400 when pincode is not exactly 6 digits.

    public static async Task<IResult> CheckServiceability(string? pincode, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pincode) || pincode.Length != 6 || !pincode.All(char.IsDigit))
        {
            return Results.BadRequest(new Response
            {
                Status  = false,
                Message = new Message
                {
                    ErrorTypeCode   = ErrorMessageEnum.BadRequest,
                    ResponseMessage = "Pincode must be exactly 6 digits."
                }
            });
        }

        var brandId = u.BrandId ?? Guid.Empty;
        var r = await dispatcher.QueryAsync(new CheckServiceabilityQuery(brandId, pincode), ct);
        return Results.Ok(new SingleResponse<ServiceabilityDto> { Status = true, Data = r });
    }

    // ── Devices ───────────────────────────────────────────────────────────────

    public static async Task<IResult> RegisterDevice(RegisterDeviceRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        var brandId    = u.BrandId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.SendAsync(new RegisterDeviceCommand(customerId, brandId, req), ct);
        return Results.Ok(new SingleResponse<CustomerDeviceDto> { Status = true, Data = r });
    }

    // ── DPDP Consents ───────────────────────────────────────────────────────────

    public static async Task<IResult> GetConsents(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetMyConsentsQuery(customerId), ct);
        return Results.Ok(new ListResponse<DpdpConsentDto> { Status = true, Data = r.ToList() });
    }

    public static async Task<IResult> GrantConsent(GrantConsentRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        var brandId    = u.BrandId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.SendAsync(new GrantConsentCommand(customerId, brandId, req), ct);
        return Results.Ok(new SingleResponse<DpdpConsentDto> { Status = true, Data = r });
    }

    public static async Task<IResult> WithdrawConsent(WithdrawConsentRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        var brandId    = u.BrandId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        // null = no active granted consent found → 404.
        var r = await dispatcher.SendAsync(new WithdrawConsentCommand(customerId, brandId, req), ct);
        return r is null
            ? Results.NotFound(new Response { Status = false })
            : Results.Ok(new SingleResponse<DpdpConsentDto> { Status = true, Data = r });
    }

    // ── Push Tokens ───────────────────────────────────────────────────────────
    // POST upserts on the unique token — re-registering reactivates and re-points to this customer.
    // DELETE deactivates on logout so the Worker does not attempt delivery to a signed-out device.

    public static async Task<IResult> RegisterPushToken(RegisterPushTokenRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        var brandId    = u.BrandId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();

        await dispatcher.SendAsync(new RegisterCustomerPushTokenCommand(customerId, brandId, req.Token, req.Platform), ct);
        return Results.Ok(new Response { Status = true });
    }

    public static async Task<IResult> DeactivatePushToken([FromBody] DeactivatePushTokenRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();

        await dispatcher.SendAsync(new DeactivateCustomerPushTokenCommand(customerId, req.Token), ct);
        return Results.Ok(new Response { Status = true });
    }

    // ── Account Deletion ────────────────────────────────────────────────────────

    public static async Task<IResult> CreateDeletionRequest(CreateDeletionRequestRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        var brandId    = u.BrandId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.SendAsync(new CreateDeletionRequestCommand(customerId, brandId, req), ct);
        return Results.Ok(new SingleResponse<AccountDeletionRequestDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetDeletionRequest(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetMyDeletionRequestQuery(customerId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<AccountDeletionRequestDto> { Status = true, Data = r });
    }

    public static async Task<IResult> CancelDeletionRequest(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();
        // Null = no pending request exists (already processed, cancelled, or never created).
        var r = await dispatcher.SendAsync(new CancelDeletionRequestCommand(customerId), ct);
        return r is null
            ? Results.NotFound(new Response { Status = false })
            : Results.Ok(new SingleResponse<AccountDeletionRequestDto> { Status = true, Data = r });
    }
}

// ── Request DTOs (local to this endpoint group) ───────────────────────────────────

/// <param name="Token">Expo push token (ExponentPushToken[…] or ExpoPushToken[…]).</param>
/// <param name="Platform">"ios" or "android".</param>
public sealed record RegisterPushTokenRequest(string Token, string Platform);

/// <param name="Token">The Expo push token to deactivate.</param>
public sealed record DeactivatePushTokenRequest(string Token);
