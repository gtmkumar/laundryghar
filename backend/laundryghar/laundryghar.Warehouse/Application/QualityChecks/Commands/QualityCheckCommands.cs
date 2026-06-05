using FluentValidation;
using laundryghar.Warehouse.Application.QualityChecks.Dtos;
using MediatR;

namespace laundryghar.Warehouse.Application.QualityChecks.Commands;

public sealed record CreateQualityCheckCommand(CreateQualityCheckRequest Request, Guid? ActorId)
    : IRequest<QualityCheckDto>;

public sealed class CreateQualityCheckHandler : IRequestHandler<CreateQualityCheckCommand, QualityCheckDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateQualityCheckHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<QualityCheckDto> Handle(CreateQualityCheckCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        var garment = await _db.Garments
            .FirstOrDefaultAsync(g => g.Id == req.GarmentId && g.BrandId == brandId, ct)
            ?? throw new KeyNotFoundException($"Garment {req.GarmentId} not found.");

        var qcRound = (short)(await _db.QualityChecks
            .CountAsync(q => q.GarmentId == req.GarmentId, ct) + 1);

        var qc = new QualityCheck
        {
            Id                  = Guid.NewGuid(),
            BrandId             = brandId,
            WarehouseId         = req.WarehouseId,
            GarmentId           = req.GarmentId,
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
            CreatedBy           = cmd.ActorId,
            UpdatedBy           = cmd.ActorId
        };

        // Update garment stage based on QC result + emit outbox event
        string newStage;
        string eventType;
        if (req.Result == "pass" || req.Result == "accept_with_note")
        {
            newStage  = "packing";
            eventType = "garment.qc_passed";
        }
        else if (req.Result is "rewash" || req.RequiresRewash)
        {
            newStage  = "rewash";
            eventType = "garment.rewash";
            garment.RewashCount++;
        }
        else
        {
            newStage  = "qc";     // fail/escalate — stays in QC pending review
            eventType = "garment.qc_failed";
        }

        garment.CurrentStage = newStage;
        garment.UpdatedAt    = now;
        garment.UpdatedBy    = cmd.ActorId;
        garment.Version++;

        // Outbox event — written in same SaveChanges call
        var outbox = new OutboxEvent
        {
            Id            = Guid.NewGuid(),
            BrandId       = brandId,
            AggregateType = "garment",
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
            CreatedBy   = cmd.ActorId
        };

        // Use execution strategy to support Npgsql retry + explicit transaction
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            _db.QualityChecks.Add(qc);
            _db.OutboxEvents.Add(outbox);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return ToDto(qc);
    }

    internal static QualityCheckDto ToDto(QualityCheck q) => new(
        q.Id, q.BrandId, q.WarehouseId, q.GarmentId,
        q.BatchId, q.QcRound, q.InspectorUserId,
        q.InspectedAt, q.Result, q.RequiresRewash,
        q.RewashPriority, q.Notes, q.Status, q.CreatedAt);
}

public sealed class CreateQualityCheckValidator : AbstractValidator<CreateQualityCheckCommand>
{
    private static readonly string[] AllowedResults =
        ["pass","fail","rewash","escalate","accept_with_note"];

    public CreateQualityCheckValidator()
    {
        RuleFor(x => x.Request.GarmentId).NotEmpty();
        RuleFor(x => x.Request.WarehouseId).NotEmpty();
        RuleFor(x => x.Request.InspectorUserId).NotEmpty();
        RuleFor(x => x.Request.Issues).NotEmpty().WithMessage("Issues JSON array is required.");
        RuleFor(x => x.Request.Result)
            .Must(r => AllowedResults.Contains(r))
            .WithMessage($"Result must be one of: {string.Join(", ", AllowedResults)}.");
    }
}
