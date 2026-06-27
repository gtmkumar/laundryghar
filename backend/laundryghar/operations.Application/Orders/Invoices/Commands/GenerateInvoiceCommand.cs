using System.Text.Json;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Orders.Invoices.Dtos;

namespace operations.Application.Orders.Invoices.Commands;

/// <summary>
/// Generates (or returns existing) a GST tax invoice for an order.
///
/// Idempotency: if an invoice already exists for the order, it is returned as-is.
///
/// Billable states: ready | delivered | closed
///   "ready" is included for POS/walk-in prepaid scenarios where the item is
///   collected at the counter immediately after processing — the operator may
///   want to print the invoice before delivery has been formally recorded.
///   "delivered" and "closed" are the standard post-fulfilment states.
///
/// Tax math source: use CGST/SGST/IGST values already on the Order entity
///   (computed by CreateOrderCommand at placement time from OrdersSettings.TaxRatePercent).
///   This avoids re-deriving totals and stays consistent with what the customer paid.
///
/// Permission: orders.update — consistent with UpdateOrderStatusCommand and
///   other admin mutations that change order-level data.
/// </summary>
public sealed record GenerateInvoiceCommand(Guid OrderId, Guid? ActorId) : ICommand<InvoiceDto>;

public sealed class GenerateInvoiceHandler : ICommandHandler<GenerateInvoiceCommand, InvoiceDto>
{
    // Billable statuses — invoice can be generated for these.
    private static readonly HashSet<string> BillableStatuses =
        [OrderStatus.Ready, OrderStatus.Delivered, OrderStatus.Closed];

    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GenerateInvoiceHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<InvoiceDto> HandleAsync(GenerateInvoiceCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        // ── 1. Load existing invoice (idempotency) ─────────────────────────────
        var existing = await _db.Invoices
            .FirstOrDefaultAsync(i => i.OrderId == cmd.OrderId && i.BrandId == brandId, ct);
        if (existing is not null)
            return ToDto(existing);

        // ── 2. Load order with related entities ────────────────────────────────
        var order = await _db.Orders
            .Include(o => o.Store)
                .ThenInclude(s => s.Franchise)
            .Include(o => o.Customer)
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == cmd.OrderId && o.BrandId == brandId, ct)
            ?? throw new KeyNotFoundException($"Order {cmd.OrderId} not found.");

        // ── 3. Validate billable state ─────────────────────────────────────────
        if (!BillableStatuses.Contains(order.Status))
            throw new BusinessRuleException(
                $"Invoice can only be generated for orders in status: " +
                $"{string.Join(", ", BillableStatuses)}. " +
                $"Current status: '{order.Status}'.");

        var now  = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        // ── 4. Supplier snapshot (store address, franchise GSTIN) ─────────────
        var store     = order.Store;
        var franchise = store.Franchise;

        var supplierName    = store.Name;
        var supplierAddress = BuildStoreAddress(store);
        var supplierGstin   = franchise.Gstin; // null if unregistered

        // ── 5. Customer snapshot ───────────────────────────────────────────────
        var customer     = order.Customer;
        var customerName = customer.DisplayName
                           ?? $"{customer.FirstName} {customer.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(customerName))
            customerName = customer.PhoneE164;

        // ── 6. Place of supply — use store's state (supplier state) ────────────
        // Per GST rules, place of supply for services is the state where the
        // service is performed (laundry/dry-cleaning = store's state).
        // CGST/SGST applies when supplier state == place of supply.
        // We default to intra-state (CGST+SGST) — IGST would require explicit flag.
        var placeOfSupply = store.State;

        // ── 7. Tax amounts — read from order entity (computed at placement) ────
        // The order already has CGST/SGST/IGST fields populated by CreateOrderCommand.
        // We use these directly rather than recomputing to maintain consistency with
        // what the customer was charged.
        var cgstAmount = order.Cgst;
        var sgstAmount = order.Sgst;
        var igstAmount = order.Igst;

        // Derive rates from amounts (safe: if both are zero, default to 9%)
        var cgstRate = cgstAmount > 0 && order.TaxableAmount > 0
            ? Math.Round(cgstAmount / order.TaxableAmount * 100m, 2)
            : (igstAmount > 0 ? 0m : InvoiceTaxCalculator.DefaultHalfRate);
        var sgstRate = cgstRate;
        var igstRate = igstAmount > 0 && order.TaxableAmount > 0
            ? Math.Round(igstAmount / order.TaxableAmount * 100m, 2)
            : 0m;

        // ── 8. Line items snapshot ─────────────────────────────────────────────
        var lineItems = order.OrderItems
            .OrderBy(i => i.LineNumber)
            .Select(i => new
            {
                description  = $"{i.ItemNameSnapshot} ({i.ServiceNameSnapshot})",
                qty          = i.Quantity,
                unit         = i.UnitOfMeasure,
                unit_price   = i.UnitPrice,
                taxable_value = i.LineSubtotal   // pre-tax line value
            })
            .ToList();

        var lineItemsJson = JsonSerializer.Serialize(lineItems);

        // ── 9. Generate invoice number ─────────────────────────────────────────
        var fy = InvoiceTaxCalculator.IndianFiscalYear(today);
        var invoiceNumber = await GenerateInvoiceNumberAsync(brandId, store.Id, store.Code, fy, ct);

        // ── 10. Persist ────────────────────────────────────────────────────────
        var invoice = new Invoice
        {
            Id              = Guid.NewGuid(),
            BrandId         = brandId,
            OrderId         = order.Id,
            InvoiceNumber   = invoiceNumber,
            InvoiceDate     = today,
            SupplierName    = supplierName,
            SupplierAddress = supplierAddress,
            SupplierGstin   = supplierGstin,
            CustomerName    = customerName,
            CustomerPhone   = customer.PhoneE164,
            CustomerGstin   = null,   // B2C default; extend via separate endpoint if needed
            PlaceOfSupply   = placeOfSupply,
            SacCode         = InvoiceTaxCalculator.DefaultSacCode,
            LineItems       = lineItemsJson,
            Subtotal        = order.Subtotal,
            DiscountTotal   = order.DiscountTotal,
            TaxableTotal    = order.TaxableAmount,
            Tax = new laundryghar.SharedDataModel.Common.TaxBreakdown
            {
                CgstRate = cgstRate, CgstAmount = cgstAmount,
                SgstRate = sgstRate, SgstAmount = sgstAmount,
                IgstRate = igstRate, IgstAmount = igstAmount,
            },
            RoundOff        = order.RoundOff,
            GrandTotal      = order.GrandTotal,
            Status          = "issued",
            CreatedAt       = now,
            CreatedBy       = cmd.ActorId
        };

        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            _db.Invoices.Add(invoice);
            await _db.SaveChangesAsync(innerCt);
        }, ct);

        return ToDto(invoice);
    }

    private async Task<string> GenerateInvoiceNumberAsync(
        Guid brandId, Guid storeId, string storeCode, int fy, CancellationToken ct)
    {
        return await _db.SqlQueryScalarAsync<string>(
            $"SELECT order_lifecycle.next_invoice_number({brandId}, {storeId}, {storeCode}, {fy}) AS \"Value\"",
            ct);
    }

    private static string BuildStoreAddress(Store store)
    {
        var parts = new[]
        {
            store.AddressLine1,
            store.AddressLine2,
            store.Landmark,
            store.City,
            store.State,
            store.Pincode
        };
        return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    internal static InvoiceDto ToDto(Invoice inv)
    {
        var lineItems = DeserializeLineItems(inv.LineItems);
        return new InvoiceDto(
            inv.Id,
            inv.OrderId,
            inv.InvoiceNumber,
            inv.InvoiceDate,
            inv.SupplierName,
            inv.SupplierAddress,
            inv.SupplierGstin,
            inv.CustomerName,
            inv.CustomerPhone,
            inv.CustomerGstin,
            inv.PlaceOfSupply,
            inv.SacCode,
            lineItems,
            inv.Subtotal,
            inv.DiscountTotal,
            inv.TaxableTotal,
            inv.Tax.CgstRate,
            inv.Tax.CgstAmount,
            inv.Tax.SgstRate,
            inv.Tax.SgstAmount,
            inv.Tax.IgstRate,
            inv.Tax.IgstAmount,
            inv.RoundOff,
            inv.GrandTotal,
            inv.Status,
            inv.CreatedAt
        );
    }

    private static IReadOnlyList<InvoiceLineItemDto> DeserializeLineItems(string json)
    {
        try
        {
            var rows = JsonSerializer.Deserialize<List<JsonElement>>(json) ?? [];
            return rows.Select(e => new InvoiceLineItemDto(
                e.TryGetProperty("description",   out var d) ? d.GetString() ?? "" : "",
                e.TryGetProperty("qty",           out var q) ? q.GetDecimal()     : 0,
                e.TryGetProperty("unit",          out var u) ? u.GetString() ?? "" : "pcs",
                e.TryGetProperty("unit_price",    out var p) ? p.GetDecimal()     : 0,
                e.TryGetProperty("taxable_value", out var t) ? t.GetDecimal()     : 0
            )).ToList().AsReadOnly();
        }
        catch
        {
            return [];
        }
    }
}
