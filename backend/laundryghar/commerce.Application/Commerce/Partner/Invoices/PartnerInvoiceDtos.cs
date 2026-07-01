namespace commerce.Application.Commerce.Partner.Invoices;

/// <summary>List-row read model for a partner invoice (GET /partner/invoices).</summary>
public sealed record PartnerInvoiceListItemDto(
    Guid Id,
    string InvoiceNumber,
    DateTimeOffset BillingPeriodStart,
    DateTimeOffset BillingPeriodEnd,
    decimal GrandTotal,
    decimal AmountPaid,
    decimal? AmountDue,
    string CurrencyCode,
    string Status,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? DueAt,
    DateTimeOffset? PaidAt);

/// <summary>Full read model for a single partner invoice (GET /partner/invoices/{id}).</summary>
public sealed record PartnerInvoiceDto(
    Guid Id,
    Guid PartnerId,
    string InvoiceNumber,
    DateTimeOffset BillingPeriodStart,
    DateTimeOffset BillingPeriodEnd,
    string LineItems,
    decimal Subtotal,
    decimal TaxTotal,
    decimal GrandTotal,
    decimal AmountPaid,
    decimal? AmountDue,
    string CurrencyCode,
    string Status,
    string? InvoicePdfUrl,
    string? PaymentLinkUrl,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? DueAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Response for POST /partner/invoices/{id}/pay — the payable Razorpay short URL.</summary>
public sealed record PayPartnerInvoiceResponse(Guid InvoiceId, string PaymentLinkUrl, string Status);
