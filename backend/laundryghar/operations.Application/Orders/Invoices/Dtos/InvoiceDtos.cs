namespace operations.Application.Orders.Invoices.Dtos;

// ── Line-item snapshot used inside InvoiceDto and the PDF renderer ─────────────

public sealed record InvoiceLineItemDto(
    string Description,
    decimal Qty,
    string Unit,
    decimal UnitPrice,
    decimal TaxableValue
);

// ── Response DTO ───────────────────────────────────────────────────────────────

public sealed record InvoiceDto(
    Guid Id,
    Guid OrderId,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    string SupplierName,
    string SupplierAddress,
    string? SupplierGstin,
    string CustomerName,
    string CustomerPhone,
    string? CustomerGstin,
    string PlaceOfSupply,
    string SacCode,
    IReadOnlyList<InvoiceLineItemDto> LineItems,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxableTotal,
    decimal CgstRate,
    decimal CgstAmount,
    decimal SgstRate,
    decimal SgstAmount,
    decimal IgstRate,
    decimal IgstAmount,
    decimal RoundOff,
    decimal GrandTotal,
    string Status,
    DateTimeOffset CreatedAt
);
