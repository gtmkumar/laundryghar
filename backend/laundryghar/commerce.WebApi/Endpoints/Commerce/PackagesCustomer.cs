using commerce.Application.Commerce;
using commerce.Application.Commerce.Customer.Packages;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Caching;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace commerce.WebApi.Endpoints.Commerce;

/// <summary>Customer — packages (CustomerOnly). customerId/brandId derived from ICurrentUser claims.</summary>
public class PackagesCustomer : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/customer/packages";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Customer - Packages");
        group.RequireAuthorization("CustomerOnly");

        // Available packages: depends only on the caller's brand (folded into the tenant cache
        // key) — the customer id passed to the query is unused for filtering. "My packages" and
        // per-package usage below are per-user and are NOT cached.
        group.MapGet(GetAvailable, "/")
            .CacheSharedOutput(CommerceCacheTags.Packages, TimeSpan.FromMinutes(10));
        group.MapGet(GetMine, "/my");
        group.MapGet(GetUsage, "/my/{id:guid}/usage");
        group.MapPost(PurchaseInitiate, "/purchase/initiate");
        group.MapPost(PurchaseVerify, "/purchase/verify");
    }

    public static async Task<IResult> GetAvailable(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (u.UserId is not { } customerId) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetAvailablePackagesQuery(customerId, u.BrandId ?? Guid.Empty), ct);
        return Results.Ok(new ListResponse<PackageDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetMine(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (u.UserId is not { } customerId) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetMyPackagesQuery(customerId, u.BrandId ?? Guid.Empty), ct);
        return Results.Ok(new ListResponse<CustomerPackageDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetUsage(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20)
    {
        if (u.UserId is not { } customerId) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetMyPackageUsageQuery(
            customerId, id, u.BrandId ?? Guid.Empty, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<PackageUsageLedgerDto> { Status = true, Data = r });
    }

    public static async Task<IResult> PurchaseInitiate(
        HttpContext http, PurchasePackageRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (u.UserId is not { } customerId) return Results.Unauthorized();
        var brandId = u.BrandId ?? Guid.Empty;
        var idempotencyKey = GetIdempotencyKey(http) ?? $"pkg_{customerId}_{req.PackageId}_{Guid.NewGuid():N}";
        var r = await dispatcher.SendAsync(new PurchasePackageInitiateCommand(customerId, brandId, req, idempotencyKey), ct);
        return Results.Ok(new SingleResponse<PaymentDto> { Status = true, Data = r });
    }

    public static async Task<IResult> PurchaseVerify(
        VerifyPaymentRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        if (u.UserId is not { } customerId) return Results.Unauthorized();
        var r = await dispatcher.SendAsync(new PurchasePackageVerifyCommand(customerId, u.BrandId ?? Guid.Empty, req), ct);
        return Results.Ok(new SingleResponse<CustomerPackageDto> { Status = true, Data = r });
    }

    /// <summary>Reads Idempotency-Key request header; returns null if absent or blank.</summary>
    internal static string? GetIdempotencyKey(HttpContext http)
    {
        if (http.Request.Headers.TryGetValue("Idempotency-Key", out var val))
        {
            var key = val.ToString().Trim();
            return string.IsNullOrEmpty(key) ? null : key;
        }
        return null;
    }
}
