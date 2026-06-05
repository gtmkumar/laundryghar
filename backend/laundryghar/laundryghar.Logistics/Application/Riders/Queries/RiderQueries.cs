using laundryghar.Logistics.Application.Riders.Commands;
using laundryghar.Logistics.Application.Riders.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Logistics.Application.Riders.Queries;

// ── List Riders ───────────────────────────────────────────────────────────────

public sealed record GetRidersQuery(int Page, int PageSize, string? Status, Guid? FranchiseId)
    : IRequest<PaginatedList<RiderDto>>;

public sealed class GetRidersHandler : IRequestHandler<GetRidersQuery, PaginatedList<RiderDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetRidersHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<RiderDto>> Handle(GetRidersQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.Riders.Where(r => r.BrandId == brandId);

        if (!string.IsNullOrEmpty(q.Status))
            query = query.Where(r => r.Status == q.Status);
        if (q.FranchiseId.HasValue)
            query = query.Where(r => r.FranchiseId == q.FranchiseId.Value);

        return PaginatedList<RiderDto>.CreateAsync(
            query.OrderByDescending(r => r.CreatedAt)
                 .Select(r => CreateRiderHandler.ToDto(r)),
            q.Page, q.PageSize, ct);
    }
}

// ── Get Rider By Id ───────────────────────────────────────────────────────────

public sealed record GetRiderByIdQuery(Guid Id) : IRequest<RiderDto?>;

public sealed class GetRiderByIdHandler : IRequestHandler<GetRiderByIdQuery, RiderDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetRiderByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<RiderDto?> Handle(GetRiderByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var r = await _db.Riders
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return r is null ? null : CreateRiderHandler.ToDto(r);
    }
}
