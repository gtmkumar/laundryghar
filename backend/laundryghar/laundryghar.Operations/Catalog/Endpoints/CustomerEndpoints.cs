using laundryghar.Catalog.Infrastructure.Auth;
using laundryghar.Catalog.Infrastructure.Services;
using laundryghar.Catalog.Application.Catalog.Commands;
using laundryghar.Catalog.Application.Catalog.Dtos;
using laundryghar.Catalog.Application.Catalog.Queries;
using laundryghar.Catalog.Application.Customer.Self.Commands;
using laundryghar.Catalog.Application.Customer.Self.Dtos;
using laundryghar.Catalog.Application.Customer.Self.Queries;
using laundryghar.Catalog.Application.Pricing.Dtos;
using laundryghar.Catalog.Application.Pricing.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace laundryghar.Catalog.Endpoints;

/// <summary>
/// Customer-facing endpoints — require CustomerOnly policy (token_use=customer).
/// All queries are self-filtered: sub claim (ClaimTypes.NameIdentifier) = customerId.
/// Brand context comes from token brand_id claim (RLS ensures brand isolation automatically).
/// </summary>
public static class CustomerEndpoints
{
    public static RouteGroupBuilder MapCustomerEndpoints(this RouteGroupBuilder group)
    {
        // ── Catalog reads (read-only, no pagination needed for customers) ──────
        var catalog = group.MapGroup("/catalog").WithTags("Customer - Catalog");

        catalog.MapGet("/categories", async (ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetServiceCategoriesQuery(1, 100), ct);
            return Results.Ok(new ListResponse<ServiceCategoryDto> { Status = true, Data = r.List.ToList() });
        }).RequireAuthorization("CustomerOnly");

        catalog.MapGet("/services", async (Guid? categoryId, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetServicesQuery(1, 100, categoryId), ct);
            return Results.Ok(new ListResponse<ServiceDto> { Status = true, Data = r.List.ToList() });
        }).RequireAuthorization("CustomerOnly");

        catalog.MapGet("/price-list", async (ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetPublishedPriceListQuery(), ct);
            return Results.Ok(new ListResponse<PriceListItemDto> { Status = true, Data = r.ToList() });
        }).RequireAuthorization("CustomerOnly");

        // ── Profile ───────────────────────────────────────────────────────────
        var profile = group.MapGroup("/profile").WithTags("Customer - Profile");

        profile.MapGet("/", async (HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new GetMyProfileQuery(customerId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<CustomerProfileDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        profile.MapPatch("/", async (HttpContext http, PatchProfileRequest req, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new PatchMyProfileCommand(customerId, req), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<CustomerProfileDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        // ── Addresses ─────────────────────────────────────────────────────────
        var addresses = group.MapGroup("/addresses").WithTags("Customer - Addresses");

        addresses.MapGet("/", async (HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new GetMyAddressesQuery(customerId), ct);
            return Results.Ok(new ListResponse<CustomerAddressDto> { Status = true, Data = r.ToList() });
        }).RequireAuthorization("CustomerOnly");

        addresses.MapPost("/", async (HttpContext http, CreateAddressRequest req, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            var brandId    = GetBrandId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new CreateMyAddressCommand(customerId, brandId, req), ct);
            return Results.Created($"/api/v1/customer/addresses/{r.Id}",
                new SingleResponse<CustomerAddressDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        addresses.MapPut("/{id:guid}", async (HttpContext http, Guid id, UpdateAddressRequest req, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new UpdateMyAddressCommand(customerId, id, req), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<CustomerAddressDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        addresses.MapDelete("/{id:guid}", async (HttpContext http, Guid id, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var ok = await sender.Send(new DeleteMyAddressCommand(customerId, id), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("CustomerOnly");

        // ── Serviceability ────────────────────────────────────────────────────
        // GET /api/v1/customer/serviceability?pincode=110001
        // Returns { serviceable: true } when any active store in this brand covers the pincode,
        // OR the pincode is listed in at least one active territory.
        // Self-explanatory 400 when pincode is not exactly 6 digits.

        var serviceability = group.MapGroup("/serviceability").WithTags("Customer - Serviceability");

        serviceability.MapGet("/", async (
            string? pincode, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(pincode) ||
                pincode.Length != 6 ||
                !pincode.All(char.IsDigit))
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

            var brandId = GetBrandId(http);
            var r = await sender.Send(new CheckServiceabilityQuery(brandId, pincode), ct);
            return Results.Ok(new SingleResponse<ServiceabilityDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        // ── Devices ───────────────────────────────────────────────────────────
        var devices = group.MapGroup("/devices").WithTags("Customer - Devices");

        devices.MapPost("/", async (HttpContext http, RegisterDeviceRequest req, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            var brandId    = GetBrandId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new RegisterDeviceCommand(customerId, brandId, req), ct);
            return Results.Ok(new SingleResponse<CustomerDeviceDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        // ── DPDP Consents ─────────────────────────────────────────────────────
        var consents = group.MapGroup("/consents").WithTags("Customer - DPDP Consents");

        consents.MapGet("/", async (HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new GetMyConsentsQuery(customerId), ct);
            return Results.Ok(new ListResponse<DpdpConsentDto> { Status = true, Data = r.ToList() });
        }).RequireAuthorization("CustomerOnly");

        consents.MapPost("/grant", async (HttpContext http, GrantConsentRequest req, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            var brandId    = GetBrandId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new GrantConsentCommand(customerId, brandId, req), ct);
            return Results.Ok(new SingleResponse<DpdpConsentDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        consents.MapPost("/withdraw", async (HttpContext http, WithdrawConsentRequest req, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            var brandId    = GetBrandId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            // H2: null = no active granted consent found → 404.
            var r = await sender.Send(new WithdrawConsentCommand(customerId, brandId, req), ct);
            return r is null
                ? Results.NotFound(new Response { Status = false })
                : Results.Ok(new SingleResponse<DpdpConsentDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        // ── Push Tokens ───────────────────────────────────────────────────────
        // Expo push token registration / deactivation for customer devices.
        // Both endpoints are CustomerOnly (JWT sub = customerId).
        // POST upserts on the unique token — re-registering reactivates and re-points to this customer.
        // DELETE deactivates on logout so the Worker does not attempt delivery to a signed-out device.
        var push = group.MapGroup("/push-token").WithTags("Customer - Push Tokens");

        push.MapPost("/", async (HttpContext http, RegisterPushTokenRequest req, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            var brandId    = GetBrandId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();

            await sender.Send(
                new RegisterCustomerPushTokenCommand(customerId, brandId, req.Token, req.Platform), ct);

            return Results.Ok(new Response { Status = true });
        }).RequireAuthorization("CustomerOnly");

        push.MapDelete("/", async (HttpContext http, [FromBody] DeactivatePushTokenRequest req, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();

            await sender.Send(
                new DeactivateCustomerPushTokenCommand(customerId, req.Token), ct);

            return Results.Ok(new Response { Status = true });
        }).RequireAuthorization("CustomerOnly");

        // ── Account Deletion ──────────────────────────────────────────────────
        var account = group.MapGroup("/account").WithTags("Customer - Account");

        account.MapPost("/deletion-request", async (HttpContext http, CreateDeletionRequestRequest req, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            var brandId    = GetBrandId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new CreateDeletionRequestCommand(customerId, brandId, req), ct);
            return Results.Ok(new SingleResponse<AccountDeletionRequestDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        account.MapGet("/deletion-request", async (HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new GetMyDeletionRequestQuery(customerId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<AccountDeletionRequestDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        account.MapDelete("/deletion-request", async (HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            // Null = no pending request exists (already processed, cancelled, or never created).
            var r = await sender.Send(new CancelDeletionRequestCommand(customerId), ct);
            return r is null
                ? Results.NotFound(new Response { Status = false })
                : Results.Ok(new SingleResponse<AccountDeletionRequestDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        return group;
    }

    /// <summary>
    /// Reads customer id from sub claim (mapped to ClaimTypes.NameIdentifier by JwtBearer).
    /// Returns Guid.Empty if absent or unparseable.
    /// </summary>
    private static Guid GetCustomerId(HttpContext http)
    {
        var sub = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    private static Guid GetBrandId(HttpContext http)
    {
        var raw = http.User.FindFirstValue("brand_id");
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

/// <param name="Token">Expo push token (ExponentPushToken[…] or ExpoPushToken[…]).</param>
/// <param name="Platform">"ios" or "android".</param>
public sealed record RegisterPushTokenRequest(string Token, string Platform);

/// <param name="Token">The Expo push token to deactivate.</param>
public sealed record DeactivatePushTokenRequest(string Token);
