using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using operations.Application.Catalog.Customer.Self.Dtos;
using operations.Application.Common.Interfaces;
using operations.Application.Common.Settings;
using operations.Application.Orders.Common;

namespace operations.Application.Catalog.Customer.Self.Queries;

// ── Profile ───────────────────────────────────────────────────────────────────

public sealed record GetMyProfileQuery(Guid CustomerId) : IQuery<CustomerProfileDto?>;

public sealed class GetMyProfileHandler : IQueryHandler<GetMyProfileQuery, CustomerProfileDto?>
{
    private readonly IOperationsDbContext _db;

    public GetMyProfileHandler(IOperationsDbContext db) => _db = db;

    public async Task<CustomerProfileDto?> HandleAsync(GetMyProfileQuery q, CancellationToken ct)
    {
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == q.CustomerId, ct);
        return c is null ? null : ToDto(c);
    }

    internal static CustomerProfileDto ToDto(laundryghar.SharedDataModel.Entities.CustomerCatalog.Customer c) => new(
        c.Id, c.BrandId, c.CustomerCode, c.PhoneE164, c.Email, c.FirstName, c.LastName, c.DisplayName,
        c.Gender, c.DateOfBirth, c.AvatarUrl, c.Locale, c.Timezone,
        c.MarketingOptIn, c.SmsOptIn, c.WhatsappOptIn, c.EmailOptIn, c.PushOptIn,
        c.LoyaltyPointsBalance, c.WalletBalance, c.Status, c.CreatedAt);
}

// ── Addresses ─────────────────────────────────────────────────────────────────

public sealed record GetMyAddressesQuery(Guid CustomerId) : IQuery<IReadOnlyList<CustomerAddressDto>>;

public sealed class GetMyAddressesHandler : IQueryHandler<GetMyAddressesQuery, IReadOnlyList<CustomerAddressDto>>
{
    private readonly IOperationsDbContext _db;

    public GetMyAddressesHandler(IOperationsDbContext db) => _db = db;

    public async Task<IReadOnlyList<CustomerAddressDto>> HandleAsync(GetMyAddressesQuery q, CancellationToken ct)
    {
        return await _db.CustomerAddresses
            .Where(a => a.CustomerId == q.CustomerId)
            .OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.CreatedAt)
            .Select(a => ToDto(a))
            .ToListAsync(ct);
    }

    internal static CustomerAddressDto ToDto(CustomerAddress a) => new(
        a.Id, a.CustomerId, a.Label, a.CustomLabel, a.RecipientName, a.RecipientPhone,
        a.AddressLine1, a.AddressLine2, a.Landmark, a.Floor, a.FlatNumber, a.BuildingName,
        a.Society, a.Area, a.City, a.State, a.Pincode, a.CountryCode,
        a.DeliveryInstructions, a.IsDefault, a.IsVerified, a.Status, a.CreatedAt);
}

// ── Consents ──────────────────────────────────────────────────────────────────

public sealed record GetMyConsentsQuery(Guid CustomerId) : IQuery<IReadOnlyList<DpdpConsentDto>>;

public sealed class GetMyConsentsHandler : IQueryHandler<GetMyConsentsQuery, IReadOnlyList<DpdpConsentDto>>
{
    private readonly IOperationsDbContext _db;

    public GetMyConsentsHandler(IOperationsDbContext db) => _db = db;

    public async Task<IReadOnlyList<DpdpConsentDto>> HandleAsync(GetMyConsentsQuery q, CancellationToken ct)
    {
        return await _db.DpdpConsents
            .Where(c => c.CustomerId == q.CustomerId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => ToDto(c))
            .ToListAsync(ct);
    }

    internal static DpdpConsentDto ToDto(DpdpConsent c) => new(
        c.Id, c.Purpose, c.PurposeDescription, c.DataCategories, c.ConsentStatus,
        c.ConsentMethod, c.PrivacyPolicyVersion, c.GrantedAt, c.WithdrawnAt, c.CreatedAt);
}

// ── Serviceability ────────────────────────────────────────────────────────────

public sealed record CheckServiceabilityQuery(Guid BrandId, string Pincode) : IQuery<ServiceabilityDto>;

public sealed class CheckServiceabilityHandler : IQueryHandler<CheckServiceabilityQuery, ServiceabilityDto>
{
    private readonly IOperationsDbContext _db;

    public CheckServiceabilityHandler(IOperationsDbContext db) => _db = db;

    public async Task<ServiceabilityDto> HandleAsync(CheckServiceabilityQuery q, CancellationToken ct)
    {
        // 1. Check an active store in this brand covers the pincode directly.
        var storeMatch = await _db.Stores
            .AnyAsync(s => s.BrandId == q.BrandId
                        && s.Pincode == q.Pincode
                        && s.Status  == "active"
                        && s.DeletedAt == null, ct);

        if (storeMatch)
            return new ServiceabilityDto(true);

        // 2. Check if the pincode falls within any active territory in this brand.
        var territoryMatch = await _db.Territories
            .AnyAsync(t => t.BrandId == q.BrandId
                        && t.Status  == "active"
                        && t.DeletedAt == null
                        && t.Pincodes.Contains(q.Pincode), ct);

        return new ServiceabilityDto(territoryMatch);
    }
}

// ── Customer catalog config ─────────────────────────────────────────────────────

/// <summary>
/// Resolves the scope-aware order/catalog rules the customer app needs up front:
/// the effective minimum order value (orders/min_order_value), the effective currency
/// (orders/currency_code, falling back to the Orders default), and the high-value garment
/// threshold (catalog/high_value_garment_threshold). Scope is the caller's brand plus an
/// optional store; the store's franchise is derived from the store row. An unknown/foreign
/// store falls back to brand-level resolution (no error).
/// </summary>
public sealed record GetCustomerCatalogConfigQuery(Guid BrandId, Guid? StoreId)
    : IQuery<CustomerCatalogConfigDto>;

public sealed class GetCustomerCatalogConfigHandler
    : IQueryHandler<GetCustomerCatalogConfigQuery, CustomerCatalogConfigDto>
{
    private readonly IOperationsDbContext _db;
    private readonly OrdersSettings _orders;

    public GetCustomerCatalogConfigHandler(IOperationsDbContext db, IOptions<OrdersSettings> orders)
    {
        _db = db;
        _orders = orders.Value;
    }

    public async Task<CustomerCatalogConfigDto> HandleAsync(GetCustomerCatalogConfigQuery q, CancellationToken ct)
    {
        // Derive the store's franchise for franchise-scope precedence. A store id that is not
        // found in the caller's brand is ignored (resolve at brand scope) rather than erroring —
        // this is a read-only config lookup.
        Guid? franchiseId = null;
        Guid? storeId = null;
        if (q.StoreId is { } sid)
        {
            var store = await _db.Stores
                .AsNoTracking()
                .Where(s => s.Id == sid && s.BrandId == q.BrandId)
                .Select(s => new { s.Id, s.FranchiseId })
                .FirstOrDefaultAsync(ct);
            if (store is not null)
            {
                storeId = store.Id;
                franchiseId = store.FranchiseId;
            }
        }

        var minOrderValue = await SettingsResolver.GetDecimalAsync(
            _db, q.BrandId, franchiseId, storeId,
            SettingCategories.Orders, SettingKeys.MinOrderValue, ct);

        var currency = await SettingsResolver.GetStringAsync(
            _db, q.BrandId, franchiseId, storeId,
            SettingCategories.Orders, SettingKeys.CurrencyCode, ct);

        var highValueThreshold = await SettingsResolver.GetDecimalAsync(
            _db, q.BrandId, franchiseId, storeId,
            SettingCategories.Catalog, SettingKeys.HighValueGarmentThreshold, ct);

        return new CustomerCatalogConfigDto(
            MinOrderValue: minOrderValue,
            CurrencyCode: currency ?? _orders.DefaultCurrencyCode,
            HighValueGarmentThreshold: highValueThreshold);
    }
}

// ── Account Deletion Request ──────────────────────────────────────────────────

public sealed record GetMyDeletionRequestQuery(Guid CustomerId) : IQuery<AccountDeletionRequestDto?>;

public sealed class GetMyDeletionRequestHandler : IQueryHandler<GetMyDeletionRequestQuery, AccountDeletionRequestDto?>
{
    private readonly IOperationsDbContext _db;

    public GetMyDeletionRequestHandler(IOperationsDbContext db) => _db = db;

    public async Task<AccountDeletionRequestDto?> HandleAsync(GetMyDeletionRequestQuery q, CancellationToken ct)
    {
        var e = await _db.AccountDeletionRequests
            .Where(r => r.CustomerId == q.CustomerId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return e is null ? null : new AccountDeletionRequestDto(
            e.Id, e.Status, e.RequestSource, e.Reason, e.RequestedAt, e.GracePeriodEndsAt, e.CancelledAt);
    }
}
