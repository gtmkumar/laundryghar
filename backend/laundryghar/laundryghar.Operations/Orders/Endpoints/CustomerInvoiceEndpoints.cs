using laundryghar.Orders.Application.Invoices;
using laundryghar.Orders.Application.Invoices.Queries;
using MediatR;

namespace laundryghar.Orders.Endpoints;

/// <summary>
/// Customer-facing invoice endpoints.
/// All require CustomerOnly policy (token_use=customer).
/// Self-filter: customerId is always derived from sub claim (never from URL).
/// IDOR protection: GetMyInvoiceQuery verifies order.customer_id == JWT sub before
/// returning the invoice — mirrors GetMyOrderByIdQuery exactly.
/// </summary>
public static class CustomerInvoiceEndpoints
{
    public static RouteGroupBuilder MapCustomerInvoiceEndpoints(this RouteGroupBuilder group)
    {
        var orders = group.MapGroup("/orders").WithTags("Customer - Invoices");

        // ── GET /orders/{id}/invoice.pdf  → downloadable PDF ──────────────────
        orders.MapGet("/{id:guid}/invoice.pdf", async (
            Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var customerId = GetCustomerId(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();

            var inv = await sender.Send(new GetMyInvoiceQuery(id, customerId), ct);
            if (inv is null) return Results.NotFound();

            var pdfBytes = InvoicePdfRenderer.Render(inv);
            var fileName = $"{inv.InvoiceNumber}.pdf";
            return Results.File(pdfBytes, "application/pdf", fileName);
        }).RequireAuthorization("CustomerOnly");

        return group;
    }

    private static Guid GetCustomerId(HttpContext http)
    {
        var sub = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
