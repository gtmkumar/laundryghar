namespace commerce.Application.Finance.Royalty.Dtos;

// ── Requests ──────────────────────────────────────────────────────────────────

/// <summary>
/// Generate/calculate a royalty invoice for a franchise for a billing period.
/// Approach: accepts a grossRevenue figure (or reads from payments if not provided).
/// royaltyPercent, marketingFeePercent, technologyFeeAmount come from the request
/// (normally sourced from the franchise agreement).
/// </summary>
public sealed record GenerateRoyaltyInvoiceRequest(
    Guid     FranchiseId,
    Guid?    FranchiseAgreementId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    /// <summary>
    /// If provided, used as gross revenue. If null, the service sums paid payments
    /// for the franchise in the period from commerce.payments.
    /// </summary>
    decimal? GrossRevenueOverride,
    decimal  RoyaltyPercent,           // e.g. 8.0 for 8%
    decimal  MarketingFeePercent,      // e.g. 2.0 for 2%
    decimal  TechnologyFeeAmount,
    decimal  OtherCharges,
    decimal  Adjustments,
    decimal  GstRate,                  // e.g. 18.0 for 18% applied on subtotal
    string?  Notes,
    string   CurrencyCode);            // INR

public sealed record IssueRoyaltyInvoiceRequest(string? Notes);

public sealed record RecordRoyaltyPaymentRequest(
    decimal AmountPaid,
    string? Notes);

// ── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record RoyaltyInvoiceDto(
    Guid             Id,
    Guid             BrandId,
    Guid             FranchiseId,
    Guid?            FranchiseAgreementId,
    string           InvoiceNumber,
    DateOnly         PeriodStart,
    DateOnly         PeriodEnd,
    decimal          GrossRevenue,
    decimal          EligibleRevenue,
    decimal          RoyaltyPercent,
    decimal          RoyaltyAmount,
    decimal          MarketingFeePercent,
    decimal          MarketingFeeAmount,
    decimal          TechnologyFeeAmount,
    decimal          OtherCharges,
    decimal          Adjustments,
    decimal          Subtotal,
    decimal          TaxTotal,
    decimal          GrandTotal,
    decimal          AmountPaid,
    decimal?         AmountDue,
    string           CurrencyCode,
    int              TotalOrders,
    DateOnly         InvoiceDate,
    DateOnly         DueDate,
    string           Status,
    string?          Notes,
    DateTimeOffset   CreatedAt,
    IReadOnlyList<RoyaltyCalculationDto> Calculations);

public sealed record RoyaltyCalculationDto(
    Guid     Id,
    Guid     RoyaltyInvoiceId,
    Guid?    StoreId,
    Guid?    OrderId,
    DateOnly CalculationDate,
    string   RevenueType,
    decimal  GrossAmount,
    decimal  ExcludedAmount,
    decimal  EligibleAmount,
    decimal  RoyaltyRate,
    decimal  RoyaltyAmount,
    string?  Notes);
