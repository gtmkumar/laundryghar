using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using operations.Application.Orders.Invoices;
using operations.Application.Orders.Invoices.Queries;

namespace operations.WebApi.Endpoints.Orders;

/// <summary>
/// Customer-facing invoice endpoints.
/// All require CustomerOnly policy (token_use=customer).
/// Self-filter: customerId is always derived from the JWT sub (ICurrentUser.UserId) (never from URL).
/// IDOR protection: GetMyInvoiceQuery verifies order.customer_id == JWT sub before
/// returning the invoice — mirrors GetMyOrderByIdQuery exactly.
/// </summary>
public class CustomerInvoiceEndpoints : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/customer/orders";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Customer - Invoices").RequireAuthorization("CustomerOnly");

        // ── GET /orders/{id}/invoice.pdf  → downloadable PDF ──────────────────
        group.MapGet(GetMyInvoicePdf, "/{id:guid}/invoice.pdf");
    }

    public static async Task<IResult> GetMyInvoicePdf(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var customerId = u.UserId ?? Guid.Empty;
        if (customerId == Guid.Empty) return Results.Unauthorized();

        var inv = await dispatcher.QueryAsync(new GetMyInvoiceQuery(id, customerId), ct);
        if (inv is null) return Results.NotFound();

        var pdfBytes = InvoicePdfRenderer.Render(inv);
        var fileName = $"{inv.InvoiceNumber}.pdf";
        return Results.File(pdfBytes, "application/pdf", fileName);
    }
}
