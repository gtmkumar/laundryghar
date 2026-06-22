using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Fulfillment;
using operations.Application.Logistics.Common;
using operations.Application.Orders.Common;
using operations.Application.Orders.Orders.Commands;
using operations.Application.Orders.Orders.Dtos;

namespace operations.Application.Orders.Orders.Queries;

// ── Admin queries ─────────────────────────────────────────────────────────────

public sealed record GetOrdersQuery(
    int Page, int PageSize,
    string? Status, Guid? StoreId,
    DateOnly? DateFrom, DateOnly? DateTo,
    string? StatusGroup = null
) : IQuery<PaginatedList<OrderDto>>;

public sealed class GetOrdersHandler : IQueryHandler<GetOrdersQuery, PaginatedList<OrderDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    /// <summary>Terminal statuses — an order in any of these is "done" and lands in history.</summary>
    private static readonly string[] TerminalStatuses =
    {
        OrderStatus.Delivered,
        OrderStatus.Cancelled,
        OrderStatus.Closed,
        OrderStatus.Returned,
    };

    public GetOrdersHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PaginatedList<OrderDto>> HandleAsync(GetOrdersQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.Orders.Where(o => o.BrandId == brandId);

        // A specific status filter takes precedence; otherwise the statusGroup
        // splits the list into the live "active" board vs terminal "history".
        if (!string.IsNullOrEmpty(q.Status))
        {
            query = query.Where(o => o.Status == q.Status);
        }
        else if (string.Equals(q.StatusGroup, "active", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(o => !TerminalStatuses.Contains(o.Status));
        }
        else if (string.Equals(q.StatusGroup, "history", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(o => TerminalStatuses.Contains(o.Status));
        }

        if (q.StoreId.HasValue)                query = query.Where(o => o.StoreId == q.StoreId.Value);

        // Date-only bounds are interpreted in the operator's local timezone (the scoped
        // store's, or Asia/Kolkata when unscoped) and converted to UTC instants before
        // filtering placed_at — otherwise a "today" filter drops orders placed in the
        // pre-dawn local hours that fall on the previous UTC day.
        if (q.DateFrom.HasValue || q.DateTo.HasValue)
        {
            var tz = LocalDateRange.Resolve(await ResolveTimeZoneIdAsync(brandId, q.StoreId, ct));

            if (q.DateFrom.HasValue)
            {
                var fromUtc = LocalDateRange.StartUtc(q.DateFrom.Value, tz);
                query = query.Where(o => o.PlacedAt >= fromUtc);
            }
            if (q.DateTo.HasValue)
            {
                var toUtcExclusive = LocalDateRange.EndUtcExclusive(q.DateTo.Value, tz);
                query = query.Where(o => o.PlacedAt < toUtcExclusive);
            }
        }

        return await PaginatedList<OrderDto>.CreateAsync(
            query.OrderByDescending(o => o.CreatedAt).Select(o => CreateOrderHandler.ToDto(o)),
            q.Page, q.PageSize, ct);
    }

    /// <summary>
    /// Returns the IANA timezone id to interpret date-only filter bounds in: the scoped
    /// store's when a storeId filter is present, otherwise null (caller falls back to the
    /// platform default). Brand-scoped lookup so a foreign storeId can never be read.
    /// </summary>
    private async Task<string?> ResolveTimeZoneIdAsync(Guid brandId, Guid? storeId, CancellationToken ct)
    {
        if (!storeId.HasValue) return null;
        return await _db.Stores
            .Where(s => s.Id == storeId.Value && s.BrandId == brandId)
            .Select(s => s.Timezone)
            .FirstOrDefaultAsync(ct);
    }
}

public sealed record GetOrderByIdQuery(Guid Id) : IQuery<OrderDto?>;

public sealed class GetOrderByIdHandler : IQueryHandler<GetOrderByIdQuery, OrderDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IFulfillmentStrategyResolver _strategies;

    public GetOrderByIdHandler(IOperationsDbContext db, ICurrentUser user, IFulfillmentStrategyResolver strategies)
    { _db = db; _user = user; _strategies = strategies; }

    public async Task<OrderDto?> HandleAsync(GetOrderByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == q.Id && o.BrandId == brandId, ct);
        if (order is null) return null;

        var items   = await _db.OrderItems.Where(i => i.OrderId == q.Id && i.BrandId == brandId).ToListAsync(ct);
        var addons  = await _db.OrderAddons.Where(a => a.OrderId == q.Id).ToListAsync(ct);
        var history = await _db.OrderStatusHistories.Where(h => h.OrderId == q.Id && h.BrandId == brandId)
                          .OrderBy(h => h.ChangedAt).ToListAsync(ct);

        // H4: expose DeliveryOtp to owning customer only, only while out_for_delivery.
        // DEF: surface allowedTransitions (next legal statuses) on the admin detail view.
        return CreateOrderHandler.ToDto(order, items, addons, history,
            includeDeliveryOtp: true, includeAllowedTransitions: true,
            statusStrategy: _strategies.ResolveForOrder(order));
    }
}

public sealed record GetOrderNotesQuery(Guid OrderId) : IQuery<IReadOnlyList<OrderNoteDto>>;

public sealed class GetOrderNotesHandler : IQueryHandler<GetOrderNotesQuery, IReadOnlyList<OrderNoteDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetOrderNotesHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<IReadOnlyList<OrderNoteDto>> HandleAsync(GetOrderNotesQuery q, CancellationToken ct)
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
public sealed record GetMyOrdersQuery(Guid CustomerId, int Page, int PageSize) : IQuery<PaginatedList<OrderDto>>;

public sealed class GetMyOrdersHandler : IQueryHandler<GetMyOrdersQuery, PaginatedList<OrderDto>>
{
    private readonly IOperationsDbContext _db;

    public GetMyOrdersHandler(IOperationsDbContext db) => _db = db;

    public Task<PaginatedList<OrderDto>> HandleAsync(GetMyOrdersQuery q, CancellationToken ct)
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
public sealed record GetMyOrderByIdQuery(Guid OrderId, Guid CustomerId) : IQuery<OrderDto?>;

public sealed class GetMyOrderByIdHandler : IQueryHandler<GetMyOrderByIdQuery, OrderDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly IFulfillmentStrategyResolver _strategies;

    public GetMyOrderByIdHandler(IOperationsDbContext db, IFulfillmentStrategyResolver strategies)
    { _db = db; _strategies = strategies; }

    public async Task<OrderDto?> HandleAsync(GetMyOrderByIdQuery q, CancellationToken ct)
    {
        // Self-filter: customer_id AND brand scoping from RLS; explicit predicate = defense-in-depth
        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == q.OrderId && o.CustomerId == q.CustomerId, ct);
        if (order is null) return null;

        var items   = await _db.OrderItems.Where(i => i.OrderId == q.OrderId).ToListAsync(ct);
        var addons  = await _db.OrderAddons.Where(a => a.OrderId == q.OrderId).ToListAsync(ct);
        var history = await _db.OrderStatusHistories.Where(h => h.OrderId == q.OrderId)
                          .OrderBy(h => h.ChangedAt).ToListAsync(ct);

        // allowedTransitions is derived purely from order status, so it is safe to share
        // with the customer detail view (no admin-only data leaks through it).
        return CreateOrderHandler.ToDto(order, items, addons, history,
            includeAllowedTransitions: true,
            statusStrategy: _strategies.ResolveForOrder(order));
    }
}

public sealed record GetMyOrderTrackingQuery(Guid OrderId, Guid CustomerId)
    : IQuery<IReadOnlyList<OrderStatusHistoryDto>?>;

public sealed class GetMyOrderTrackingHandler
    : IQueryHandler<GetMyOrderTrackingQuery, IReadOnlyList<OrderStatusHistoryDto>?>
{
    private readonly IOperationsDbContext _db;

    public GetMyOrderTrackingHandler(IOperationsDbContext db) => _db = db;

    public async Task<IReadOnlyList<OrderStatusHistoryDto>?> HandleAsync(
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
