using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Batches.Commands.CreateWarehouseBatch;
using operations.Application.Warehouse.Batches.Dtos;

namespace operations.Application.Warehouse.Batches.Commands.UpdateWarehouseBatch;

// ── Update Batch ──────────────────────────────────────────────────────────────

public sealed record UpdateWarehouseBatchCommand(Guid Id, UpdateWarehouseBatchRequest Request, Guid? ActorId)
    : ICommand<WarehouseBatchDto?>;

public sealed class UpdateWarehouseBatchCommandHandler
    : ICommandHandler<UpdateWarehouseBatchCommand, WarehouseBatchDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateWarehouseBatchCommandHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<WarehouseBatchDto?> HandleAsync(UpdateWarehouseBatchCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var batch = await _db.WarehouseBatches
            .FirstOrDefaultAsync(b => b.Id == command.Id && b.BrandId == brandId, cancellationToken);
        if (batch is null) return null;

        // Include the batch's brand so a brand-scoped admin matches via the ancestor id.
        if (!_user.IsWithinScope(brandId: batch.BrandId, warehouseId: batch.WarehouseId))
            throw new ForbiddenException("This warehouse batch is outside your assigned scope.");

        var now = DateTimeOffset.UtcNow;
        var req = command.Request;

        if (req.Status == "running" && batch.StartedAt is null)
            batch.StartedAt = now;
        if (req.Status is "completed" or "failed" or "aborted")
            batch.CompletedAt = now;

        if (req.MachineId is not null) batch.MachineId     = req.MachineId;
        if (req.FailureReason is not null) batch.FailureReason = req.FailureReason;
        batch.Status    = req.Status;
        batch.UpdatedAt = now;
        batch.UpdatedBy = command.ActorId;

        await _db.SaveChangesAsync(cancellationToken);
        return CreateWarehouseBatchCommandHandler.ToDto(batch);
    }
}

public sealed class UpdateWarehouseBatchValidator : AbstractValidator<UpdateWarehouseBatchRequest>
{
    // Mirrors warehouse_batches.status CHECK constraint values.
    private static readonly string[] AllowedStatuses =
        ["created", "running", "completed", "failed", "aborted"];

    public UpdateWarehouseBatchValidator()
    {
        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => AllowedStatuses.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", AllowedStatuses)}.");
    }
}
