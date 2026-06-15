using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Processes.Dtos;

namespace operations.Application.Warehouse.Processes.Commands.CreateProcessLog;

// ── Process Log (scan event) ──────────────────────────────────────────────────

public sealed record CreateProcessLogCommand(CreateProcessLogRequest Request, Guid? ActorId)
    : ICommand<ProcessLogEntryDto>;

public sealed class CreateProcessLogCommandHandler : ICommandHandler<CreateProcessLogCommand, ProcessLogEntryDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreateProcessLogCommandHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<ProcessLogEntryDto> HandleAsync(CreateProcessLogCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var req     = command.Request;
        var now     = DateTimeOffset.UtcNow;

        // Validate garment belongs to brand
        var garment = await _db.Garments
            .FirstOrDefaultAsync(g => g.Id == req.GarmentId && g.BrandId == brandId, cancellationToken)
            ?? throw new KeyNotFoundException($"Garment {req.GarmentId} not found.");

        // OccurredAt = partition key — let DB/EF set it from now(); do NOT manually manage partition.
        var log = new ProcessLog
        {
            Id              = Guid.NewGuid(),
            OccurredAt      = now,   // partition-key — DB routes to correct partition
            BrandId         = brandId,
            WarehouseId     = req.WarehouseId,
            BatchId         = req.BatchId,
            GarmentId       = req.GarmentId,
            TagCode         = garment.TagCode,
            ProcessId       = req.ProcessId,
            ProcessCode     = req.ProcessCode,
            Action          = req.Action,
            FromStage       = req.FromStage ?? garment.CurrentStage,
            ToStage         = req.ToStage,
            PerformedByUserId = command.ActorId,
            PerformedByName   = req.PerformedByName,
            Metadata          = "{}",
            CreatedAt         = now,
            CreatedBy         = command.ActorId
        };

        // Advance garment stage if ToStage is provided
        if (!string.IsNullOrEmpty(req.ToStage))
        {
            garment.CurrentStage  = req.ToStage;
            garment.LastScannedAt = now;
            garment.LastScannedBy = command.ActorId;
            garment.UpdatedAt     = now;
            garment.UpdatedBy     = command.ActorId;
            garment.Version++;
        }

        _db.ProcessLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);

        return new ProcessLogEntryDto(
            log.Id, log.BrandId, log.WarehouseId, log.GarmentId,
            log.TagCode, log.ProcessCode, log.Action,
            log.FromStage, log.ToStage, log.OccurredAt, log.CreatedAt);
    }
}

public sealed class CreateProcessLogValidator : AbstractValidator<CreateProcessLogRequest>
{
    private static readonly string[] AllowedActions =
        ["scan_in","scan_out","start","complete","transfer","hold","release","flag","rewash"];

    public CreateProcessLogValidator()
    {
        RuleFor(x => x.GarmentId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.ProcessCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Action)
            .Must(a => AllowedActions.Contains(a))
            .WithMessage($"Action must be one of: {string.Join(", ", AllowedActions)}.");
    }
}
