using laundryghar.Orders.Infrastructure.Auth;
using laundryghar.Orders.Infrastructure.Services;
using laundryghar.Orders.Application.Invoices.Commands;
using laundryghar.Orders.Application.Invoices.Dtos;
using MediatR;

namespace laundryghar.Orders.Application.Invoices.Queries;

/// <summary>Returns the invoice for an order, or null if none exists yet.</summary>
public sealed record GetInvoiceQuery(Guid OrderId) : IRequest<InvoiceDto?>;

public sealed class GetInvoiceHandler : IRequestHandler<GetInvoiceQuery, InvoiceDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetInvoiceHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<InvoiceDto?> Handle(GetInvoiceQuery query, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var invoice = await _db.Invoices
            .FirstOrDefaultAsync(i => i.OrderId == query.OrderId && i.BrandId == brandId, ct);

        return invoice is null ? null : GenerateInvoiceHandler.ToDto(invoice);
    }
}

/// <summary>Customer self-filtered invoice retrieval (CustomerOnly lane).</summary>
public sealed record GetMyInvoiceQuery(Guid OrderId, Guid CustomerId) : IRequest<InvoiceDto?>;

public sealed class GetMyInvoiceHandler : IRequestHandler<GetMyInvoiceQuery, InvoiceDto?>
{
    private readonly LaundryGharDbContext _db;

    public GetMyInvoiceHandler(LaundryGharDbContext db)
    {
        _db = db;
    }

    public async Task<InvoiceDto?> Handle(GetMyInvoiceQuery query, CancellationToken ct)
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
