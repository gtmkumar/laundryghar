namespace laundryghar.Catalog.Application.Customer.Admin.Dtos;

public sealed record AdminCustomerDto(
    Guid Id,
    Guid BrandId,
    string CustomerCode,
    string PhoneE164,
    string? Email,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? Gender,
    DateOnly? DateOfBirth,
    string Locale,
    string Timezone,
    int LifetimeOrders,
    decimal LifetimeSpend,
    int LoyaltyPointsBalance,
    decimal WalletBalance,
    string? CustomerSegment,
    string? RiskFlag,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record AdminUpdateCustomerRequest(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Gender,
    DateOnly? DateOfBirth,
    string? CustomerSegment,
    string? RiskFlag
);
