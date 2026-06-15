using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Assignments.Commands.CreateRiderAssignment;
using operations.Application.Logistics.Assignments.Dtos;

namespace operations.Application.Logistics.Assignments.Queries.GetAssignmentById;

// ── Get Assignment By Id ──────────────────────────────────────────────────────

public sealed record GetAssignmentByIdQuery(Guid Id) : IQuery<RiderAssignmentDto?>;

public sealed class GetAssignmentByIdHandler : IQueryHandler<GetAssignmentByIdQuery, RiderAssignmentDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetAssignmentByIdHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<RiderAssignmentDto?> HandleAsync(GetAssignmentByIdQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var a = await _db.RiderAssignments
            .FirstOrDefaultAsync(x => x.Id == query.Id && x.BrandId == brandId, cancellationToken);
        return a is null ? null : CreateRiderAssignmentHandler.ToDto(a);
    }
}
