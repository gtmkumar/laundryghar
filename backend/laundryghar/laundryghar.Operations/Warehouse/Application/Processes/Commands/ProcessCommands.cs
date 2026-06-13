using FluentValidation;
using laundryghar.Warehouse.Application.Processes.Dtos;
using MediatR;

namespace laundryghar.Warehouse.Application.Processes.Commands;

// ── Warehouse Process CRUD ────────────────────────────────────────────────────

public sealed record CreateWarehouseProcessCommand(CreateWarehouseProcessRequest Request, Guid? ActorId)
    : IRequest<WarehouseProcessDto>;

public sealed class CreateWarehouseProcessHandler
    : IRequestHandler<CreateWarehouseProcessCommand, WarehouseProcessDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateWarehouseProcessHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<WarehouseProcessDto> Handle(CreateWarehouseProcessCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        var e = new WarehouseProcess
        {
            Id                  = Guid.NewGuid(),
            BrandId             = brandId,
            Code                = req.Code,
            Name                = req.Name,
            NameLocalized       = req.NameLocalized,
            ProcessCategory     = req.ProcessCategory,
            SequenceOrder       = req.SequenceOrder,
            ExpectedDurationMin = req.ExpectedDurationMin,
            RequiresMachine     = req.RequiresMachine,
            RequiresSupervisor  = req.RequiresSupervisor,
            IsActive            = true,
            CreatedAt           = now,
            CreatedBy           = cmd.ActorId
        };

        _db.WarehouseProcesses.Add(e);
        await _db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    internal static WarehouseProcessDto ToDto(WarehouseProcess p) => new(
        p.Id, p.BrandId, p.Code, p.Name, p.ProcessCategory,
        p.SequenceOrder, p.ExpectedDurationMin, p.RequiresMachine,
        p.RequiresSupervisor, p.IsActive, p.CreatedAt);
}

// ── Process Log (scan event) ──────────────────────────────────────────────────

public sealed record CreateProcessLogCommand(CreateProcessLogRequest Request, Guid? ActorId)
    : IRequest<ProcessLogEntryDto>;

public sealed class CreateProcessLogHandler : IRequestHandler<CreateProcessLogCommand, ProcessLogEntryDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateProcessLogHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<ProcessLogEntryDto> Handle(CreateProcessLogCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        // Validate garment belongs to brand
        var garment = await _db.Garments
            .FirstOrDefaultAsync(g => g.Id == req.GarmentId && g.BrandId == brandId, ct)
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
            PerformedByUserId = cmd.ActorId,
            PerformedByName   = req.PerformedByName,
            Metadata          = "{}",
            CreatedAt         = now,
            CreatedBy         = cmd.ActorId
        };

        // Advance garment stage if ToStage is provided
        if (!string.IsNullOrEmpty(req.ToStage))
        {
            garment.CurrentStage  = req.ToStage;
            garment.LastScannedAt = now;
            garment.LastScannedBy = cmd.ActorId;
            garment.UpdatedAt     = now;
            garment.UpdatedBy     = cmd.ActorId;
            garment.Version++;
        }

        _db.ProcessLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        return new ProcessLogEntryDto(
            log.Id, log.BrandId, log.WarehouseId, log.GarmentId,
            log.TagCode, log.ProcessCode, log.Action,
            log.FromStage, log.ToStage, log.OccurredAt, log.CreatedAt);
    }
}

public sealed class CreateProcessLogValidator : AbstractValidator<CreateProcessLogCommand>
{
    private static readonly string[] AllowedActions =
        ["scan_in","scan_out","start","complete","transfer","hold","release","flag","rewash"];

    public CreateProcessLogValidator()
    {
        RuleFor(x => x.Request.GarmentId).NotEmpty();
        RuleFor(x => x.Request.WarehouseId).NotEmpty();
        RuleFor(x => x.Request.ProcessCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Action)
            .Must(a => AllowedActions.Contains(a))
            .WithMessage($"Action must be one of: {string.Join(", ", AllowedActions)}.");
    }
}

public sealed class CreateWarehouseProcessValidator : AbstractValidator<CreateWarehouseProcessCommand>
{
    private static readonly string[] AllowedCategories =
        ["receiving","sorting","pre_treatment","washing","drying","ironing","quality_check","packing","dispatch"];

    public CreateWarehouseProcessValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.NameLocalized).NotEmpty();
        RuleFor(x => x.Request.ProcessCategory)
            .Must(c => AllowedCategories.Contains(c))
            .WithMessage($"ProcessCategory must be one of: {string.Join(", ", AllowedCategories)}.");
    }
}
