using FluentValidation;
using laundryghar.Logistics.Application.Assignments.Dtos;
using laundryghar.SharedDataModel.Enums;
using MediatR;

namespace laundryghar.Logistics.Application.Assignments.Commands;

// ── Create Assignment ─────────────────────────────────────────────────────────

public sealed record CreateRiderAssignmentCommand(CreateRiderAssignmentRequest Request, Guid? ActorId)
    : IRequest<RiderAssignmentDto>;

public sealed class CreateRiderAssignmentHandler
    : IRequestHandler<CreateRiderAssignmentCommand, RiderAssignmentDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateRiderAssignmentHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RiderAssignmentDto> Handle(CreateRiderAssignmentCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        // Verify rider belongs to this brand
        var riderExists = await _db.Riders
            .AnyAsync(r => r.Id == req.RiderId && r.BrandId == brandId, ct);
        if (!riderExists)
            throw new BusinessRuleException("Rider not found under the current brand.");

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
            CreatedBy           = cmd.ActorId
        };

        _db.RiderAssignments.Add(assignment);
        await _db.SaveChangesAsync(ct);
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

// ── Update Assignment ─────────────────────────────────────────────────────────

public sealed record UpdateRiderAssignmentCommand(Guid Id, UpdateRiderAssignmentRequest Request, Guid? ActorId)
    : IRequest<RiderAssignmentDto?>;

public sealed class UpdateRiderAssignmentHandler
    : IRequestHandler<UpdateRiderAssignmentCommand, RiderAssignmentDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateRiderAssignmentHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RiderAssignmentDto?> Handle(UpdateRiderAssignmentCommand cmd, CancellationToken ct)
    {
        var brandId    = _user.RequireBrandId();
        var assignment = await _db.RiderAssignments
            .FirstOrDefaultAsync(a => a.Id == cmd.Id && a.BrandId == brandId, ct);
        if (assignment is null) return null;

        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        if (req.Status       is not null) assignment.Status       = req.Status;
        if (req.MaxPickups   is not null) assignment.MaxPickups   = req.MaxPickups.Value;
        if (req.MaxDeliveries is not null) assignment.MaxDeliveries = req.MaxDeliveries.Value;
        if (req.Notes        is not null) assignment.Notes        = req.Notes;
        if (req.ActualStartAt is not null) assignment.ActualStartAt = req.ActualStartAt;
        if (req.ActualEndAt  is not null) assignment.ActualEndAt  = req.ActualEndAt;

        assignment.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        return CreateRiderAssignmentHandler.ToDto(assignment);
    }
}

// ── Validators ────────────────────────────────────────────────────────────────

public sealed class CreateRiderAssignmentValidator : AbstractValidator<CreateRiderAssignmentCommand>
{
    public CreateRiderAssignmentValidator()
    {
        RuleFor(x => x.Request.RiderId).NotEmpty();
        RuleFor(x => x.Request.StoreId).NotEmpty();
        RuleFor(x => x.Request.MaxPickups).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.MaxDeliveries).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.ShiftEnd).GreaterThan(x => x.Request.ShiftStart)
            .WithMessage("ShiftEnd must be after ShiftStart.");
    }
}
