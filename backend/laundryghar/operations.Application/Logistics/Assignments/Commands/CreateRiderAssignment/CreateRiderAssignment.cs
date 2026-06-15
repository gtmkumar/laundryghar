using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Assignments.Dtos;

namespace operations.Application.Logistics.Assignments.Commands.CreateRiderAssignment;

// ── Create Assignment ─────────────────────────────────────────────────────────

public sealed record CreateRiderAssignmentCommand(CreateRiderAssignmentRequest Request, Guid? ActorId)
    : ICommand<RiderAssignmentDto>;

public sealed class CreateRiderAssignmentHandler
    : ICommandHandler<CreateRiderAssignmentCommand, RiderAssignmentDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreateRiderAssignmentHandler(IOperationsDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RiderAssignmentDto> HandleAsync(CreateRiderAssignmentCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var req     = command.Request;
        var now     = DateTimeOffset.UtcNow;

        // Verify rider belongs to this brand
        var riderExists = await _db.Riders
            .AnyAsync(r => r.Id == req.RiderId && r.BrandId == brandId, cancellationToken);
        if (!riderExists)
            throw new BusinessRuleException("Rider not found under the current brand.");

        // Cross-brand guard: the shift store must belong to this brand too.
        var storeExists = await _db.Stores
            .AnyAsync(s => s.Id == req.StoreId && s.BrandId == brandId, cancellationToken);
        if (!storeExists)
            throw new BusinessRuleException("Store not found under the current brand.");

        var assignment = new RiderAssignment
        {
            Id                  = Guid.NewGuid(),
            RiderId             = req.RiderId,
            BrandId             = brandId,
            StoreId             = req.StoreId,
            ShiftDate           = req.ShiftDate,
            ShiftStart          = req.ShiftStart,
            ShiftEnd            = req.ShiftEnd,
            MaxPickups          = req.MaxPickups,
            MaxDeliveries       = req.MaxDeliveries,
            CompletedPickups    = 0,
            CompletedDeliveries = 0,
            FailedAttempts      = 0,
            Status              = RiderAssignmentStatus.Scheduled,
            Notes               = req.Notes,
            CreatedAt           = now,
            UpdatedAt           = now,
            CreatedBy           = command.ActorId
        };

        _db.RiderAssignments.Add(assignment);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(assignment);
    }

    internal static RiderAssignmentDto ToDto(RiderAssignment a) => new(
        a.Id, a.RiderId, a.BrandId, a.StoreId,
        a.ShiftDate, a.ShiftStart, a.ShiftEnd,
        a.ActualStartAt, a.ActualEndAt,
        a.MaxPickups, a.MaxDeliveries,
        a.CompletedPickups, a.CompletedDeliveries,
        a.FailedAttempts, a.TotalDistanceKm, a.Earnings,
        a.Status, a.Notes, a.CreatedAt, a.UpdatedAt);
}

public sealed class CreateRiderAssignmentRequestValidator : AbstractValidator<CreateRiderAssignmentRequest>
{
    public CreateRiderAssignmentRequestValidator()
    {
        RuleFor(x => x.RiderId).NotEmpty();
        RuleFor(x => x.StoreId).NotEmpty();
        RuleFor(x => x.MaxPickups).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxDeliveries).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ShiftEnd).GreaterThan(x => x.ShiftStart)
            .WithMessage("ShiftEnd must be after ShiftStart.");
    }
}
