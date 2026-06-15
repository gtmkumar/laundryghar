using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Orders.Invoices.Commands;
using operations.Application.Orders.Invoices.Dtos;

namespace operations.Application.Orders.Invoices.Queries;

/// <summary>Returns the invoice for an order, or null if none exists yet.</summary>
public sealed record GetInvoiceQuery(Guid OrderId) : IQuery<InvoiceDto?>;

public sealed class GetInvoiceHandler : IQueryHandler<GetInvoiceQuery, InvoiceDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetInvoiceHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<InvoiceDto?> HandleAsync(GetInvoiceQuery query, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var invoice = await _db.Invoices
            .FirstOrDefaultAsync(i => i.OrderId == query.OrderId && i.BrandId == brandId, ct);

        return invoice is null ? null : GenerateInvoiceHandler.ToDto(invoice);
    }
}

/// <summary>Customer self-filtered invoice retrieval (CustomerOnly lane).</summary>
public sealed record GetMyInvoiceQuery(Guid OrderId, Guid CustomerId) : IQuery<InvoiceDto?>;

public sealed class GetMyInvoiceHandler : IQueryHandler<GetMyInvoiceQuery, InvoiceDto?>
{
    private readonly IOperationsDbContext _db;

    public GetMyInvoiceHandler(IOperationsDbContext db)
    {
        _db = db;
    }

    public async Task<InvoiceDto?> HandleAsync(GetMyInvoiceQuery query, CancellationToken ct)
    {
        // IDOR guard: verify order belongs to this customer before returning invoice.
        var orderBelongsToCustomer = await _db.Orders
            .AnyAsync(o => o.Id == query.OrderId && o.CustomerId == query.CustomerId, ct);

        if (!orderBelongsToCustomer)
            return null;

        // RLS provides brand isolation; customer ownership verified above.
        var invoice = await _db.Invoices
            .FirstOrDefaultAsync(i => i.OrderId == query.OrderId, ct);

        return invoice is null ? null : GenerateInvoiceHandler.ToDto(invoice);
    }
}
