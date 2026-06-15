using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Assignments.Commands.CreateRiderAssignment;
using operations.Application.Logistics.Assignments.Dtos;

namespace operations.Application.Logistics.Assignments.Commands.UpdateRiderAssignment;

// ── Update Assignment ─────────────────────────────────────────────────────────

public sealed record UpdateRiderAssignmentCommand(Guid Id, UpdateRiderAssignmentRequest Request, Guid? ActorId)
    : ICommand<RiderAssignmentDto?>;

public sealed class UpdateRiderAssignmentHandler
    : ICommandHandler<UpdateRiderAssignmentCommand, RiderAssignmentDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateRiderAssignmentHandler(IOperationsDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RiderAssignmentDto?> HandleAsync(UpdateRiderAssignmentCommand command, CancellationToken cancellationToken)
    {
        var brandId    = _user.RequireBrandId();
        var assignment = await _db.RiderAssignments
            .FirstOrDefaultAsync(a => a.Id == command.Id && a.BrandId == brandId, cancellationToken);
        if (assignment is null) return null;

        var req = command.Request;
        var now = DateTimeOffset.UtcNow;

        if (req.Status       is not null) assignment.Status       = req.Status;
        if (req.MaxPickups   is not null) assignment.MaxPickups   = req.MaxPickups.Value;
        if (req.MaxDeliveries is not null) assignment.MaxDeliveries = req.MaxDeliveries.Value;
        if (req.Notes        is not null) assignment.Notes        = req.Notes;
        if (req.ActualStartAt is not null) assignment.ActualStartAt = req.ActualStartAt;
        if (req.ActualEndAt  is not null) assignment.ActualEndAt  = req.ActualEndAt;

        assignment.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);
        return CreateRiderAssignmentHandler.ToDto(assignment);
    }
}
