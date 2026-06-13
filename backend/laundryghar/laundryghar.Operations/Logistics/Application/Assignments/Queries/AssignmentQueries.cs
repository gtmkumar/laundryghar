using laundryghar.Logistics.Infrastructure.Auth;
using laundryghar.Logistics.Infrastructure.Services;
using laundryghar.Logistics.Application.Assignments.Commands;
using laundryghar.Logistics.Application.Assignments.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Logistics.Application.Assignments.Queries;

// ── List Assignments ──────────────────────────────────────────────────────────

public sealed record GetAssignmentsQuery(int Page, int PageSize, Guid? RiderId, string? Status, DateOnly? ShiftDate)
    : IRequest<PaginatedList<RiderAssignmentDto>>;

public sealed class GetAssignmentsHandler
    : IRequestHandler<GetAssignmentsQuery, PaginatedList<RiderAssignmentDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetAssignmentsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<RiderAssignmentDto>> Handle(GetAssignmentsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.RiderAssignments.Where(a => a.BrandId == brandId);

        if (q.RiderId.HasValue)
            query = query.Where(a => a.RiderId == q.RiderId.Value);
        if (!string.IsNullOrEmpty(q.Status))
            query = query.Where(a => a.Status == q.Status);
        if (q.ShiftDate.HasValue)
            query = query.Where(a => a.ShiftDate == q.ShiftDate.Value);

        return PaginatedList<RiderAssignmentDto>.CreateAsync(
            query.OrderByDescending(a => a.ShiftDate).ThenBy(a => a.ShiftStart)
                 .Select(a => CreateRiderAssignmentHandler.ToDto(a)),
            q.Page, q.PageSize, ct);
    }
}

// ── Get Assignment By Id ──────────────────────────────────────────────────────

public sealed record GetAssignmentByIdQuery(Guid Id) : IRequest<RiderAssignmentDto?>;

public sealed class GetAssignmentByIdHandler : IRequestHandler<GetAssignmentByIdQuery, RiderAssignmentDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetAssignmentByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<RiderAssignmentDto?> Handle(GetAssignmentByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var a = await _db.RiderAssignments
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return a is null ? null : CreateRiderAssignmentHandler.ToDto(a);
    }
}
