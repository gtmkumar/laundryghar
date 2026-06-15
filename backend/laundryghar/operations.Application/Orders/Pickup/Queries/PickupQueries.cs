using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Orders.Pickup.Commands;
using operations.Application.Orders.Pickup.Dtos;

namespace operations.Application.Orders.Pickup.Queries;

// ── Admin queries ──────────────────────────────────────────────────────────────

public sealed record GetPickupRequestsQuery(int Page, int PageSize, string? Status)
    : IQuery<PaginatedList<PickupRequestDto>>;

public sealed class GetPickupRequestsHandler
    : IQueryHandler<GetPickupRequestsQuery, PaginatedList<PickupRequestDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetPickupRequestsHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PaginatedList<PickupRequestDto>> HandleAsync(GetPickupRequestsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.PickupRequests.Where(p => p.BrandId == brandId);
        if (!string.IsNullOrEmpty(q.Status)) query = query.Where(p => p.Status == q.Status);

        // Materialize the page as PickupRequest entities first; ToDto (which calls
        // JsonSerializer.Deserialize on RequestedItems) runs in memory, not in SQL,
        // preventing EF from attempting to translate the JSON call as a SQL expression.
        var page = await PaginatedList<PickupRequest>.CreateAsync(
            query.OrderByDescending(p => p.CreatedAt),
            q.Page, q.PageSize, ct);

        return page.Map(CreatePickupRequestAdminHandler.ToDto);
    }
}

public sealed record GetPickupRequestByIdQuery(Guid Id) : IQuery<PickupRequestDto?>;

public sealed class GetPickupRequestByIdHandler
    : IQueryHandler<GetPickupRequestByIdQuery, PickupRequestDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetPickupRequestByIdHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PickupRequestDto?> HandleAsync(GetPickupRequestByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.PickupRequests
            .FirstOrDefaultAsync(p => p.Id == q.Id && p.BrandId == brandId, ct);
        return e is null ? null : CreatePickupRequestAdminHandler.ToDto(e);
    }
}

// ── Customer-self queries ──────────────────────────────────────────────────────

/// <summary>
/// Returns all pickup requests for the authenticated customer (self-filter: customerId from JWT sub).
/// IDOR: never trusts customerId from URL; always derives it from the token.
/// </summary>
public sealed record GetMyPickupRequestsQuery(Guid CustomerId, int Page, int PageSize, string? Status)
    : IQuery<PaginatedList<PickupRequestDto>>;

public sealed class GetMyPickupRequestsHandler
    : IQueryHandler<GetMyPickupRequestsQuery, PaginatedList<PickupRequestDto>>
{
    private readonly IOperationsDbContext _db;

    public GetMyPickupRequestsHandler(IOperationsDbContext db) => _db = db;

    public async Task<PaginatedList<PickupRequestDto>> HandleAsync(GetMyPickupRequestsQuery q, CancellationToken ct)
    {
        var query = _db.PickupRequests.Where(p => p.CustomerId == q.CustomerId);
        if (!string.IsNullOrEmpty(q.Status)) query = query.Where(p => p.Status == q.Status);

        // Same fix as admin list: materialize before mapping to avoid client-eval.
        var page = await PaginatedList<PickupRequest>.CreateAsync(
            query.OrderByDescending(p => p.CreatedAt),
            q.Page, q.PageSize, ct);

        return page.Map(CreatePickupRequestAdminHandler.ToDto);
    }
}

/// <summary>
/// Returns a single pickup request for the authenticated customer.
/// Returns null → 404 when the id exists but belongs to another customer (IDOR guard).
/// </summary>
public sealed record GetMyPickupRequestByIdQuery(Guid Id, Guid CustomerId) : IQuery<PickupRequestDto?>;

public sealed class GetMyPickupRequestByIdHandler
    : IQueryHandler<GetMyPickupRequestByIdQuery, PickupRequestDto?>
{
    private readonly IOperationsDbContext _db;

    public GetMyPickupRequestByIdHandler(IOperationsDbContext db) => _db = db;

    public async Task<PickupRequestDto?> HandleAsync(GetMyPickupRequestByIdQuery q, CancellationToken ct)
    {
        // Self-filter: customer_id predicate = defense-in-depth on top of RLS.
        var e = await _db.PickupRequests
            .FirstOrDefaultAsync(p => p.Id == q.Id && p.CustomerId == q.CustomerId, ct);
        return e is null ? null : CreatePickupRequestAdminHandler.ToDto(e);
    }
}

// ── Delivery-slot / assignment queries (shared) ────────────────────────────────

public sealed record GetDeliveryAssignmentsQuery(int Page, int PageSize)
    : IQuery<PaginatedList<DeliveryAssignmentDto>>;

public sealed class GetDeliveryAssignmentsHandler
    : IQueryHandler<GetDeliveryAssignmentsQuery, PaginatedList<DeliveryAssignmentDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetDeliveryAssignmentsHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<DeliveryAssignmentDto>> HandleAsync(GetDeliveryAssignmentsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        return PaginatedList<DeliveryAssignmentDto>.CreateAsync(
            _db.DeliveryAssignments.Where(a => a.BrandId == brandId)
               .OrderByDescending(a => a.AssignedAt)
               .Select(a => new DeliveryAssignmentDto(
                   a.Id, a.BrandId, a.StoreId, a.RiderId,
                   a.OrderId, a.PickupRequestId, a.LegType, a.AssignedAt, a.Status)),
            q.Page, q.PageSize, ct);
    }
}
