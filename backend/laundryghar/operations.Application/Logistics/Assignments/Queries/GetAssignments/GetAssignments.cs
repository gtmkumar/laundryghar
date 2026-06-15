using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Assignments.Commands.CreateRiderAssignment;
using operations.Application.Logistics.Assignments.Dtos;

namespace operations.Application.Logistics.Assignments.Queries.GetAssignments;

// ── List Assignments ──────────────────────────────────────────────────────────

public sealed record GetAssignmentsQuery(int Page, int PageSize, Guid? RiderId, string? Status, DateOnly? ShiftDate)
    : IQuery<PaginatedList<RiderAssignmentDto>>;

public sealed class GetAssignmentsHandler
    : IQueryHandler<GetAssignmentsQuery, PaginatedList<RiderAssignmentDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetAssignmentsHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<RiderAssignmentDto>> HandleAsync(GetAssignmentsQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var q       = _db.RiderAssignments.Where(a => a.BrandId == brandId);

        if (query.RiderId.HasValue)
            q = q.Where(a => a.RiderId == query.RiderId.Value);
        if (!string.IsNullOrEmpty(query.Status))
            q = q.Where(a => a.Status == query.Status);
        if (query.ShiftDate.HasValue)
            q = q.Where(a => a.ShiftDate == query.ShiftDate.Value);

        return PaginatedList<RiderAssignmentDto>.CreateAsync(
            q.OrderByDescending(a => a.ShiftDate).ThenBy(a => a.ShiftStart)
             .Select(a => CreateRiderAssignmentHandler.ToDto(a)),
            query.Page, query.PageSize, cancellationToken);
    }
}
