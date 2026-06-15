using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.Utilities.Services;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Processes.Dtos;

namespace operations.Application.Warehouse.Processes.Commands.CreateWarehouseProcess;

// ── Warehouse Process CRUD ────────────────────────────────────────────────────

public sealed record CreateWarehouseProcessCommand(CreateWarehouseProcessRequest Request, Guid? ActorId)
    : ICommand<WarehouseProcessDto>;

public sealed class CreateWarehouseProcessCommandHandler
    : ICommandHandler<CreateWarehouseProcessCommand, WarehouseProcessDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreateWarehouseProcessCommandHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<WarehouseProcessDto> HandleAsync(CreateWarehouseProcessCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var req     = command.Request;
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
            CreatedBy           = command.ActorId
        };

        _db.WarehouseProcesses.Add(e);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(e);
    }

    internal static WarehouseProcessDto ToDto(WarehouseProcess p) => new(
        p.Id, p.BrandId, p.Code, p.Name, p.ProcessCategory,
        p.SequenceOrder, p.ExpectedDurationMin, p.RequiresMachine,
        p.RequiresSupervisor, p.IsActive, p.CreatedAt);
}

public sealed class CreateWarehouseProcessValidator : AbstractValidator<CreateWarehouseProcessRequest>
{
    private static readonly string[] AllowedCategories =
        ["receiving","sorting","pre_treatment","washing","drying","ironing","quality_check","packing","dispatch"];

    public CreateWarehouseProcessValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameLocalized).NotEmpty();
        RuleFor(x => x.ProcessCategory)
            .Must(c => AllowedCategories.Contains(c))
            .WithMessage($"ProcessCategory must be one of: {string.Join(", ", AllowedCategories)}.");
    }
}
