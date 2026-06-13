using laundryghar.Logistics.Infrastructure.Auth;
using laundryghar.Logistics.Infrastructure.Services;
using laundryghar.Logistics.Application.CapacityConfigs.Commands;
using laundryghar.Logistics.Application.CapacityConfigs.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Logistics.Application.CapacityConfigs.Queries;

// ── List CapacityConfigs ──────────────────────────────────────────────────────

public sealed record GetCapacityConfigsQuery(int Page, int PageSize, Guid? RiderId, string? Status)
    : IRequest<PaginatedList<RiderCapacityConfigDto>>;

public sealed class GetCapacityConfigsHandler
    : IRequestHandler<GetCapacityConfigsQuery, PaginatedList<RiderCapacityConfigDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetCapacityConfigsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<RiderCapacityConfigDto>> Handle(GetCapacityConfigsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.RiderCapacityConfigs.Where(c => c.BrandId == brandId);

        if (q.RiderId.HasValue)
            query = query.Where(c => c.RiderId == q.RiderId.Value);
        if (!string.IsNullOrEmpty(q.Status))
            query = query.Where(c => c.Status == q.Status);

        return PaginatedList<RiderCapacityConfigDto>.CreateAsync(
            query.OrderByDescending(c => c.CreatedAt)
                 .Select(c => CreateCapacityConfigHandler.ToDto(c)),
            q.Page, q.PageSize, ct);
    }
}

// ── Get CapacityConfig By Id ──────────────────────────────────────────────────

public sealed record GetCapacityConfigByIdQuery(Guid Id) : IRequest<RiderCapacityConfigDto?>;

public sealed class GetCapacityConfigByIdHandler
    : IRequestHandler<GetCapacityConfigByIdQuery, RiderCapacityConfigDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetCapacityConfigByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<RiderCapacityConfigDto?> Handle(GetCapacityConfigByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var c = await _db.RiderCapacityConfigs
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return c is null ? null : CreateCapacityConfigHandler.ToDto(c);
    }
}
