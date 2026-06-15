using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using operations.Application.Orders.Invoices;
using operations.Application.Orders.Invoices.Commands;
using operations.Application.Orders.Invoices.Dtos;
using operations.Application.Orders.Invoices.Queries;

namespace operations.WebApi.Endpoints.Orders;

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
public class AdminInvoiceEndpoints : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/orders";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Invoices");

        group.MapGet(GetInvoice, "/{id:guid}/invoice").RequireAuthorization("permission:orders.read");
        group.MapPost(GenerateInvoice, "/{id:guid}/invoice").RequireAuthorization("permission:orders.update");
        group.MapGet(GetInvoicePdf, "/{id:guid}/invoice.pdf").RequireAuthorization("permission:orders.read");
    }

    // ── GET  /orders/{id}/invoice   → invoice JSON ─────────────────────────
    public static async Task<IResult> GetInvoice(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetInvoiceQuery(id), ct);
        return r is null
            ? Results.NotFound(new Response { Status = false,
                Message = new() { ResponseMessage = "No invoice found for this order. Use POST to generate one." } })
            : Results.Ok(new SingleResponse<InvoiceDto> { Status = true, Data = r });
    }

    // ── POST /orders/{id}/invoice   → generate invoice ─────────────────────
    public static async Task<IResult> GenerateInvoice(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new GenerateInvoiceCommand(id, u.UserId), ct);
        return Results.Created(
            $"/api/v1/admin/orders/{id}/invoice",
            new SingleResponse<InvoiceDto> { Status = true, Data = r });
    }

    // ── GET  /orders/{id}/invoice.pdf → PDF bytes ──────────────────────────
    public static async Task<IResult> GetInvoicePdf(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var inv = await dispatcher.QueryAsync(new GetInvoiceQuery(id), ct);
        if (inv is null)
            return Results.NotFound();

        var pdfBytes = InvoicePdfRenderer.Render(inv);
        var fileName = $"{inv.InvoiceNumber}.pdf";
        return Results.File(pdfBytes, "application/pdf", fileName);
    }
}
