using laundryghar.Orders.Application.Pickup.Commands;
using laundryghar.Orders.Application.Pickup.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Orders.Application.Pickup.Queries;

public sealed record GetPickupRequestsQuery(int Page, int PageSize, string? Status)
    : IRequest<PaginatedList<PickupRequestDto>>;

public sealed class GetPickupRequestsHandler
    : IRequestHandler<GetPickupRequestsQuery, PaginatedList<PickupRequestDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetPickupRequestsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<PickupRequestDto>> Handle(GetPickupRequestsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.PickupRequests.Where(p => p.BrandId == brandId);
        if (!string.IsNullOrEmpty(q.Status)) query = query.Where(p => p.Status == q.Status);

        return PaginatedList<PickupRequestDto>.CreateAsync(
            query.OrderByDescending(p => p.CreatedAt)
                 .Select(p => CreatePickupRequestAdminHandler.ToDto(p)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetPickupRequestByIdQuery(Guid Id) : IRequest<PickupRequestDto?>;

public sealed class GetPickupRequestByIdHandler
    : IRequestHandler<GetPickupRequestByIdQuery, PickupRequestDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetPickupRequestByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PickupRequestDto?> Handle(GetPickupRequestByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.PickupRequests
            .FirstOrDefaultAsync(p => p.Id == q.Id && p.BrandId == brandId, ct);
        return e is null ? null : CreatePickupRequestAdminHandler.ToDto(e);
    }
}

public sealed record GetDeliveryAssignmentsQuery(int Page, int PageSize)
    : IRequest<PaginatedList<DeliveryAssignmentDto>>;

public sealed class GetDeliveryAssignmentsHandler
    : IRequestHandler<GetDeliveryAssignmentsQuery, PaginatedList<DeliveryAssignmentDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetDeliveryAssignmentsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<DeliveryAssignmentDto>> Handle(GetDeliveryAssignmentsQuery q, CancellationToken ct)
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
