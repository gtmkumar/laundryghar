using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Common;
using operations.Application.Orders.Pickup.Dtos;

namespace operations.Application.Orders.Delivery.Commands;

public sealed record CreateDeliveryAssignmentCommand(
    CreateDeliveryAssignmentRequest Request, Guid? ActorId) : ICommand<DeliveryAssignmentDto>;

public sealed class CreateDeliveryAssignmentHandler
    : ICommandHandler<CreateDeliveryAssignmentCommand, DeliveryAssignmentDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreateDeliveryAssignmentHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<DeliveryAssignmentDto> HandleAsync(CreateDeliveryAssignmentCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        // Validate the assigned rider belongs to this brand (cross-brand IDOR guard).
        // Also fetch PrimaryStoreId for store resolution fallback below.
        var rider = await _db.Riders
            .Where(r => r.Id == req.RiderId && r.BrandId == brandId)
            .Select(r => new { r.Id, r.PrimaryStoreId })
            .FirstOrDefaultAsync(ct);
        if (rider is null)
            throw new KeyNotFoundException("Rider not found.");

        // ── Store resolution (NOT NULL FK — must not be Guid.Empty) ───────────
        // Priority: pickup request's StoreId → order's StoreId → rider's PrimaryStoreId
        //           → actor's scoped StoreId → exception (never Guid.Empty).
        Guid? resolvedStoreId = _user.StoreId;

        // Validate the linked order belongs to this brand and capture its StoreId.
        Guid? orderStoreId = null;
        if (req.OrderId.HasValue)
        {
            var order = await _db.Orders
                .Where(o => o.Id == req.OrderId.Value && o.BrandId == brandId)
                .Select(o => new { o.Id, o.StoreId })
                .FirstOrDefaultAsync(ct);
            if (order is null)
                throw new KeyNotFoundException("Order not found.");
            orderStoreId = order.StoreId;
        }

        // Validate the linked pickup request belongs to this brand and capture its StoreId.
        Guid? pickupStoreId = null;
        if (req.PickupRequestId.HasValue)
        {
            var pickup = await _db.PickupRequests
                .Where(p => p.Id == req.PickupRequestId.Value && p.BrandId == brandId)
                .Select(p => new { p.Id, p.StoreId })
                .FirstOrDefaultAsync(ct);
            if (pickup is null)
                throw new KeyNotFoundException("Pickup request not found.");
            pickupStoreId = pickup.StoreId;
        }

        resolvedStoreId = pickupStoreId
                       ?? orderStoreId
                       ?? rider.PrimaryStoreId
                       ?? resolvedStoreId;

        if (!resolvedStoreId.HasValue)
            throw new BusinessRuleException(
                "No store could be resolved for this assignment. " +
                "Ensure the pickup request, order, or rider has a store association.");

        if (!_user.IsWithinScope(brandId: brandId, storeId: resolvedStoreId.Value))
            throw new ForbiddenException("This delivery assignment is outside your assigned scope.");

        var assignment = new DeliveryAssignment
        {
            Id              = Guid.NewGuid(),
            BrandId         = brandId,
            StoreId         = resolvedStoreId.Value,  // pre-validated; never Guid.Empty
            RiderId         = req.RiderId,
            OrderId         = req.OrderId,
            OrderCreatedAt  = req.OrderCreatedAt,
            PickupRequestId = req.PickupRequestId,
            LegType         = req.LegType,
            AssignedAt      = now,
            AssignedBy      = cmd.ActorId,
            AddressSnapshot = "{}",
            OtpVerified     = false,
            Status          = "assigned",
            Metadata        = "{}",
            CreatedAt       = now,
            UpdatedAt       = now,
            CreatedBy       = cmd.ActorId,
            UpdatedBy       = cmd.ActorId
        };

        _db.DeliveryAssignments.Add(assignment);
        await _db.SaveChangesAsync(ct);

        // Bump the rider's current_load: a new assignment is now active.
        await RiderLoad.IncrementAsync(_db, req.RiderId, ct);

        return new DeliveryAssignmentDto(
            assignment.Id, assignment.BrandId, assignment.StoreId, assignment.RiderId,
            assignment.OrderId, assignment.PickupRequestId, assignment.LegType,
            assignment.AssignedAt, assignment.Status);
    }
}

public sealed record UpdateDeliveryAssignmentCommand(Guid Id, UpdateDeliveryAssignmentRequest Request, Guid? ActorId)
    : ICommand<DeliveryAssignmentDto?>;

public sealed class UpdateDeliveryAssignmentHandler
    : ICommandHandler<UpdateDeliveryAssignmentCommand, DeliveryAssignmentDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateDeliveryAssignmentHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<DeliveryAssignmentDto?> HandleAsync(UpdateDeliveryAssignmentCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.DeliveryAssignments
            .FirstOrDefaultAsync(a => a.Id == cmd.Id && a.BrandId == brandId, ct);
        if (e is null) return null;

        if (!_user.IsWithinScope(brandId: e.BrandId, storeId: e.StoreId))
            throw new ForbiddenException("This delivery assignment is outside your assigned scope.");

        var wasTerminal = DeliveryAssignmentStatusHelper.IsTerminal(e.Status);
        e.Status    = cmd.Request.Status;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;

        await _db.SaveChangesAsync(ct);

        // Decrement load when the assignment transitions INTO a terminal state
        // but was not already terminal (prevents double-decrement on re-stamps).
        if (!wasTerminal && DeliveryAssignmentStatusHelper.IsTerminal(cmd.Request.Status))
            await RiderLoad.DecrementAsync(_db, e.RiderId, ct);

        return new DeliveryAssignmentDto(
            e.Id, e.BrandId, e.StoreId, e.RiderId, e.OrderId,
            e.PickupRequestId, e.LegType, e.AssignedAt, e.Status);
    }
}

file static class DeliveryAssignmentStatusHelper
{
    internal static bool IsTerminal(string status) =>
        status is "completed" or "failed" or "cancelled";
}

public sealed class CreateDeliveryAssignmentValidator : AbstractValidator<CreateDeliveryAssignmentCommand>
{
    private static readonly string[] AllowedLegTypes = ["pickup", "delivery", "return"];

    public CreateDeliveryAssignmentValidator()
    {
        RuleFor(x => x.Request.RiderId).NotEmpty();
        RuleFor(x => x.Request.LegType)
            .Must(t => AllowedLegTypes.Contains(t))
            .WithMessage($"LegType must be one of: {string.Join(", ", AllowedLegTypes)}.");
    }
}

public sealed class UpdateDeliveryAssignmentValidator : AbstractValidator<UpdateDeliveryAssignmentCommand>
{
    // Mirrors delivery_assignments.status CHECK constraint values.
    private static readonly string[] AllowedStatuses =
        ["assigned", "accepted", "started", "arrived", "completed", "failed", "cancelled"];

    public UpdateDeliveryAssignmentValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Request.Status)
            .NotEmpty()
            .Must(s => AllowedStatuses.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", AllowedStatuses)}.");
    }
}
