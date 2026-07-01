using System.Text.Json;
using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.QualityChecks.Dtos;

namespace operations.Application.Warehouse.QualityChecks.Commands.CreateQualityCheck;

public sealed record CreateQualityCheckCommand(CreateQualityCheckRequest Request, Guid? ActorId)
    : ICommand<QualityCheckDto>;

public sealed class CreateQualityCheckCommandHandler
    : ICommandHandler<CreateQualityCheckCommand, QualityCheckDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreateQualityCheckCommandHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<QualityCheckDto> HandleAsync(CreateQualityCheckCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var req     = command.Request;
        var now     = DateTimeOffset.UtcNow;

        var garment = await _db.FulfillmentUnits
            .FirstOrDefaultAsync(g => g.Id == req.FulfillmentUnitId && g.BrandId == brandId, cancellationToken)
            ?? throw new KeyNotFoundException($"FulfillmentUnit {req.FulfillmentUnitId} not found.");

        // Pass the full ancestor chain (brand → franchise → store/warehouse) so a brand- or
        // franchise-scoped admin matches via an ancestor id rather than 403-ing on the leaf.
        if (!_user.IsWithinScope(brandId: garment.BrandId, franchiseId: garment.FranchiseId, storeId: garment.StoreId, warehouseId: garment.WarehouseId))
            throw new ForbiddenException("This garment is outside your assigned scope.");

        var qcRound = (short)(await _db.QualityChecks
            .CountAsync(q => q.FulfillmentUnitId == req.FulfillmentUnitId, cancellationToken) + 1);

        var qc = new QualityCheck
        {
            Id                  = Guid.NewGuid(),
            BrandId             = brandId,
            WarehouseId         = req.WarehouseId,
            FulfillmentUnitId           = req.FulfillmentUnitId,
            OrderId             = garment.OrderId,
            OrderCreatedAt      = garment.OrderCreatedAt,
            BatchId             = req.BatchId,
            QcRound             = qcRound,
            InspectorUserId     = req.InspectorUserId,
            InspectedAt         = now,
            Result              = req.Result,
            Issues              = req.Issues,
            PreWashInspectionId  = req.PreWashInspectionId,
            PostWashInspectionId = req.PostWashInspectionId,
            RequiresRewash      = req.RequiresRewash,
            RewashPriority      = req.RewashPriority,
            SupervisorApproval  = false,
            CustomerCommunicated = false,
            Notes               = req.Notes,
            Status              = "active",
            CreatedAt           = now,
            UpdatedAt           = now,
            CreatedBy           = command.ActorId,
            UpdatedBy           = command.ActorId
        };

        // Update garment stage based on QC result + emit outbox event
        string newStage;
        string eventType;
        if (req.Result == "pass" || req.Result == "accept_with_note")
        {
            newStage  = "packing";
            eventType = "fulfillment.qc_passed";
        }
        else if (req.Result is "rewash" || req.RequiresRewash)
        {
            newStage  = "rewash";
            eventType = "fulfillment.rewash";
            garment.Attributes.RewashCount++;
        }
        else
        {
            newStage  = "qc";     // fail/escalate — stays in QC pending review
            eventType = "fulfillment.qc_failed";
        }

        garment.CurrentStage = newStage;
        garment.UpdatedAt    = now;
        garment.UpdatedBy    = command.ActorId;
        garment.Version++;

        // Outbox event — written in same SaveChanges call
        var outbox = new OutboxEvent
        {
            Id            = Guid.NewGuid(),
            BrandId       = brandId,
            AggregateType = "fulfillment",
            AggregateId   = garment.Id,
            EventType     = eventType,
            EventVersion  = 1,
            Payload       = JsonSerializer.Serialize(new
            {
                garmentId  = garment.Id,
                brandId,
                tagCode    = garment.TagCode,
                orderId    = garment.OrderId,
                qcRound,
                result     = req.Result,
                requiresRewash = req.RequiresRewash,
                newStage,
                inspectedAt = now
            }),
            Metadata    = "{}",
            OccurredAt  = now,
            Status      = "pending",
            CreatedAt   = now,
            CreatedBy   = command.ActorId
        };

        // Use the retry-safe transaction boundary owned by the context interface
        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            _db.QualityChecks.Add(qc);
            _db.OutboxEvents.Add(outbox);
            await _db.SaveChangesAsync(innerCt);
        }, cancellationToken);

        return ToDto(qc);
    }

    internal static QualityCheckDto ToDto(QualityCheck q) => new(
        q.Id, q.BrandId, q.WarehouseId, q.FulfillmentUnitId,
        q.BatchId, q.QcRound, q.InspectorUserId,
        q.InspectedAt, q.Result, q.RequiresRewash,
        q.RewashPriority, q.Notes, q.Status, q.CreatedAt);
}

public sealed class CreateQualityCheckValidator : AbstractValidator<CreateQualityCheckRequest>
{
    private static readonly string[] AllowedResults =
        ["pass","fail","rewash","escalate","accept_with_note"];

    public CreateQualityCheckValidator()
    {
        RuleFor(x => x.FulfillmentUnitId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.InspectorUserId).NotEmpty();
        RuleFor(x => x.Issues).NotEmpty().WithMessage("Issues JSON array is required.");
        RuleFor(x => x.Result)
            .Must(r => AllowedResults.Contains(r))
            .WithMessage($"Result must be one of: {string.Join(", ", AllowedResults)}.");
    }
}
