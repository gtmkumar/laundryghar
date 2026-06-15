using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Assignments.Commands.CreateRiderAssignment;
using operations.Application.Logistics.Assignments.Dtos;

namespace operations.Application.Logistics.RiderSelf.Commands.UpdateMyAssignmentStatus;

// ── Update own assignment status ──────────────────────────────────────────────

public sealed record UpdateMyAssignmentStatusCommand(
    Guid   AssignmentId,
    Guid   UserId,
    Guid   BrandId,
    string Status) : ICommand<RiderAssignmentDto?>;

public sealed class UpdateMyAssignmentStatusHandler
    : ICommandHandler<UpdateMyAssignmentStatusCommand, RiderAssignmentDto?>
{
    private readonly IOperationsDbContext _db;
    public UpdateMyAssignmentStatusHandler(IOperationsDbContext db) => _db = db;

    public async Task<RiderAssignmentDto?> HandleAsync(UpdateMyAssignmentStatusCommand command, CancellationToken cancellationToken)
    {
        var ct  = cancellationToken;
        var cmd = command;

        // Self-resolve the rider from user_id + brand_id.
        var riderId = await _db.Riders
            .Where(r => r.UserId == cmd.UserId && r.BrandId == cmd.BrandId)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);
        if (riderId is null) return null;

        // Find the assignment — must belong to this rider AND this brand (self-filter)
        var assignment = await _db.RiderAssignments
            .FirstOrDefaultAsync(a => a.Id    == cmd.AssignmentId
                                   && a.RiderId == riderId.Value
                                   && a.BrandId == cmd.BrandId, ct);

        // Return null → endpoint returns 404 (assignment not found OR does not belong to this rider)
        if (assignment is null) return null;

        assignment.Status    = cmd.Status;
        assignment.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return CreateRiderAssignmentHandler.ToDto(assignment);
    }
}
