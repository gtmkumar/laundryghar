using laundryghar.Orders.Application.Orders.Commands;
using laundryghar.Orders.Application.Orders.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Orders.Application.Orders.Queries;

// ── Admin queries ─────────────────────────────────────────────────────────────

public sealed record GetOrdersQuery(
    int Page, int PageSize,
    string? Status, Guid? StoreId,
    DateOnly? DateFrom, DateOnly? DateTo
) : IRequest<PaginatedList<OrderDto>>;

public sealed class GetOrdersHandler : IRequestHandler<GetOrdersQuery, PaginatedList<OrderDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetOrdersHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<OrderDto>> Handle(GetOrdersQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.Orders.Where(o => o.BrandId == brandId);

        if (!string.IsNullOrEmpty(q.Status))  query = query.Where(o => o.Status == q.Status);
        if (q.StoreId.HasValue)                query = query.Where(o => o.StoreId == q.StoreId.Value);
        if (q.DateFrom.HasValue)
            query = query.Where(o => o.CreatedAt >= q.DateFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        if (q.DateTo.HasValue)
            query = query.Where(o => o.CreatedAt <= q.DateTo.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));

        return PaginatedList<OrderDto>.CreateAsync(
            query.OrderByDescending(o => o.CreatedAt).Select(o => CreateOrderHandler.ToDto(o)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetOrderByIdQuery(Guid Id) : IRequest<OrderDto?>;

public sealed class GetOrderByIdHandler : IRequestHandler<GetOrderByIdQuery, OrderDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetOrderByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<OrderDto?> Handle(GetOrderByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == q.Id && o.BrandId == brandId, ct);
        if (order is null) return null;

        var items   = await _db.OrderItems.Where(i => i.OrderId == q.Id && i.BrandId == brandId).ToListAsync(ct);
        var addons  = await _db.OrderAddons.Where(a => a.OrderId == q.Id).ToListAsync(ct);
        var history = await _db.OrderStatusHistories.Where(h => h.OrderId == q.Id && h.BrandId == brandId)
                          .OrderBy(h => h.ChangedAt).ToListAsync(ct);

        return CreateOrderHandler.ToDto(order, items, addons, history);
    }
}

public sealed record GetOrderNotesQuery(Guid OrderId) : IRequest<IReadOnlyList<OrderNoteDto>>;

public sealed class GetOrderNotesHandler : IRequestHandler<GetOrderNotesQuery, IReadOnlyList<OrderNoteDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetOrderNotesHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<IReadOnlyList<OrderNoteDto>> Handle(GetOrderNotesQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        return await _db.OrderNotes
            .Where(n => n.OrderId == q.OrderId && n.BrandId == brandId && n.DeletedAt == null)
            .OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.CreatedAt)
            .Select(n => CreateOrderNoteHandler.ToDto(n))
            .ToListAsync(ct);
    }
}

// ── Customer-self queries ─────────────────────────────────────────────────────

/// <summary>Customer can only see their own orders (self-filter by sub = customerId).</summary>
public sealed record GetMyOrdersQuery(Guid CustomerId, int Page, int PageSize) : IRequest<PaginatedList<OrderDto>>;

public sealed class GetMyOrdersHandler : IRequestHandler<GetMyOrdersQuery, PaginatedList<OrderDto>>
{
    private readonly LaundryGharDbContext _db;

    public GetMyOrdersHandler(LaundryGharDbContext db) => _db = db;

    public Task<PaginatedList<OrderDto>> Handle(GetMyOrdersQuery q, CancellationToken ct)
        => PaginatedList<OrderDto>.CreateAsync(
            _db.Orders
                .Where(o => o.CustomerId == q.CustomerId)
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => CreateOrderHandler.ToDto(o)),
            q.Page, q.PageSize, ct);
}

/// <summary>
/// IDOR guard: customerId from JWT sub must match order.customer_id.
/// Returns null → 404 if the order exists but belongs to another customer.
/// </summary>
public sealed record GetMyOrderByIdQuery(Guid OrderId, Guid CustomerId) : IRequest<OrderDto?>;

public sealed class GetMyOrderByIdHandler : IRequestHandler<GetMyOrderByIdQuery, OrderDto?>
{
    private readonly LaundryGharDbContext _db;

    public GetMyOrderByIdHandler(LaundryGharDbContext db) => _db = db;

    public async Task<OrderDto?> Handle(GetMyOrderByIdQuery q, CancellationToken ct)
    {
        // Self-filter: customer_id AND brand scoping from RLS; explicit predicate = defense-in-depth
        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == q.OrderId && o.CustomerId == q.CustomerId, ct);
        if (order is null) return null;

        var items   = await _db.OrderItems.Where(i => i.OrderId == q.OrderId).ToListAsync(ct);
        var addons  = await _db.OrderAddons.Where(a => a.OrderId == q.OrderId).ToListAsync(ct);
        var history = await _db.OrderStatusHistories.Where(h => h.OrderId == q.OrderId)
                          .OrderBy(h => h.ChangedAt).ToListAsync(ct);

        return CreateOrderHandler.ToDto(order, items, addons, history);
    }
}

public sealed record GetMyOrderTrackingQuery(Guid OrderId, Guid CustomerId)
    : IRequest<IReadOnlyList<OrderStatusHistoryDto>?>;

public sealed class GetMyOrderTrackingHandler
    : IRequestHandler<GetMyOrderTrackingQuery, IReadOnlyList<OrderStatusHistoryDto>?>
{
    private readonly LaundryGharDbContext _db;

    public GetMyOrderTrackingHandler(LaundryGharDbContext db) => _db = db;

    public async Task<IReadOnlyList<OrderStatusHistoryDto>?> Handle(
        GetMyOrderTrackingQuery q, CancellationToken ct)
    {
        // IDOR: ensure order belongs to customer
        var exists = await _db.Orders
            .AnyAsync(o => o.Id == q.OrderId && o.CustomerId == q.CustomerId, ct);
        if (!exists) return null;

        return await _db.OrderStatusHistories
            .Where(h => h.OrderId == q.OrderId)
            .OrderBy(h => h.ChangedAt)
            .Select(h => new OrderStatusHistoryDto(
                h.Id, h.FromStatus, h.ToStatus, h.ChangedAt, h.ChangedByType, h.Reason, h.CustomerNotified))
            .ToListAsync(ct);
    }
}
