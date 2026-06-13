using laundryghar.Orders.Application.Invoices;
using laundryghar.Orders.Application.Invoices.Commands;
using laundryghar.Orders.Application.Invoices.Queries;
using MediatR;

namespace laundryghar.Orders.Endpoints;

/// <summary>
/// Admin invoice endpoints.
///
/// Permission rationale:
///   GET  /invoice     → orders.read   — reading invoice is part of viewing the order.
///   POST /invoice     → orders.update — generating an invoice mutates order-level state
///                                       (writes invoice_number, etc.) — same gate as status updates.
///   GET  /invoice.pdf → orders.read   — PDF is just a rendered view of an existing invoice.
///
/// Reusing orders.read / orders.update avoids proliferating narrow permissions and is
/// consistent with how OrderNote endpoints reuse orders.read / orders.notes.manage.
/// </summary>
public static class AdminInvoiceEndpoints
{
    public static RouteGroupBuilder MapAdminInvoiceEndpoints(this RouteGroupBuilder group)
    {
        var orders = group.MapGroup("/orders").WithTags("Admin - Invoices");

        // ── GET  /orders/{id}/invoice   → invoice JSON ─────────────────────────
        orders.MapGet("/{id:guid}/invoice", async (
            Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetInvoiceQuery(id), ct);
            return r is null
                ? Results.NotFound(new Response { Status = false,
                    Message = new() { ResponseMessage = "No invoice found for this order. Use POST to generate one." } })
                : Results.Ok(new SingleResponse<Application.Invoices.Dtos.InvoiceDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:orders.read");

        // ── POST /orders/{id}/invoice   → generate invoice ─────────────────────
        orders.MapPost("/{id:guid}/invoice", async (
            Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GenerateInvoiceCommand(id, u.UserId), ct);
            return Results.Created(
                $"/api/v1/admin/orders/{id}/invoice",
                new SingleResponse<Application.Invoices.Dtos.InvoiceDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:orders.update");

        // ── GET  /orders/{id}/invoice.pdf → PDF bytes ──────────────────────────
        orders.MapGet("/{id:guid}/invoice.pdf", async (
            Guid id, ISender sender, CancellationToken ct) =>
        {
            var inv = await sender.Send(new GetInvoiceQuery(id), ct);
            if (inv is null)
                return Results.NotFound();

            var pdfBytes = InvoicePdfRenderer.Render(inv);
            var fileName = $"{inv.InvoiceNumber}.pdf";
            return Results.File(pdfBytes, "application/pdf", fileName);
        }).RequireAuthorization("permission:orders.read");

        return group;
    }
}
