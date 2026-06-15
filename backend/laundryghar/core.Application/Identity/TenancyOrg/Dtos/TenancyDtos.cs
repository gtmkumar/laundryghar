namespace core.Application.Identity.TenancyOrg.Dtos;

// ─── Platform ──────────────────────────────────────────────────────────────

public sealed record PlatformDto(Guid Id, string Code, string Name, string? LegalName, string Status, DateTimeOffset CreatedAt);

// ─── Franchise ─────────────────────────────────────────────────────────────

public sealed record FranchiseDto(Guid Id, Guid BrandId, string Code, string LegalName, string OnboardingStatus, string Status, DateTimeOffset CreatedAt);
public sealed record CreateFranchiseRequest(Guid BrandId, string Code, string LegalName, string ContactPhone, string? ContactEmail = null, string BillingAddress = "{}");
public sealed record UpdateFranchiseRequest(string? LegalName, string? OnboardingStatus, string? Status);

// ─── Store ─────────────────────────────────────────────────────────────────

public sealed record StoreDto(Guid Id, Guid BrandId, Guid FranchiseId, string Code, string Name, string StoreType, string City, string Status, DateTimeOffset CreatedAt);
public sealed record CreateStoreRequest(Guid BrandId, Guid FranchiseId, string Code, string Name, string AddressLine1, string City, string State, string Pincode, string StoreType = "walkin");
public sealed record UpdateStoreRequest(string? Name, string? Status, string? ContactPhone);

// ─── Warehouse ─────────────────────────────────────────────────────────────

public sealed record WarehouseDto(Guid Id, Guid BrandId, Guid FranchiseId, string Code, string Name, string City, string Status, DateTimeOffset CreatedAt);
public sealed record CreateWarehouseRequest(Guid BrandId, Guid FranchiseId, string Code, string Name, string AddressLine1, string City, string State, string Pincode, string WarehouseType = "central");
public sealed record UpdateWarehouseRequest(string? Name, string? Status, string? ContactPhone);
