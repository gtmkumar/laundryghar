using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.Utilities.Services;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Inspections.Dtos;

namespace operations.Application.Warehouse.Inspections.Commands.CreateGarmentCondition;

// ── Garment Condition CRUD ─────────────────────────────────────────────────────

public sealed record CreateGarmentConditionCommand(CreateGarmentConditionRequest Request, Guid? ActorId)
    : ICommand<GarmentConditionDto>;

public sealed class CreateGarmentConditionCommandHandler
    : ICommandHandler<CreateGarmentConditionCommand, GarmentConditionDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreateGarmentConditionCommandHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<GarmentConditionDto> HandleAsync(CreateGarmentConditionCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var req = command.Request;
        var now = DateTimeOffset.UtcNow;

        var e = new GarmentCondition
        {
            Id                 = Guid.NewGuid(),
            BrandId            = brandId,
            Code               = req.Code,
            Name               = req.Name,
            NameLocalized      = req.NameLocalized,
            Category           = req.Category,
            SeverityLevels     = ["minor","moderate","severe"],
            RequiresDisclaimer = req.RequiresDisclaimer,
            DisclaimerText     = req.DisclaimerText,
            DisplayOrder       = req.DisplayOrder,
            IsActive           = true,
            Status             = "active",
            CreatedAt          = now,
            UpdatedAt          = now,
            CreatedBy          = command.ActorId,
            UpdatedBy          = command.ActorId
        };

        _db.GarmentConditions.Add(e);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(e);
    }

    internal static GarmentConditionDto ToDto(GarmentCondition c) => new(
        c.Id, c.BrandId, c.Code, c.Name, c.Category,
        c.RequiresDisclaimer, c.DisplayOrder, c.IsActive, c.Status);
}

public sealed class CreateGarmentConditionValidator : AbstractValidator<CreateGarmentConditionRequest>
{
    private static readonly string[] AllowedCategories =
        ["stain","damage","wear","missing_part","dimensional","color","other"];

    public CreateGarmentConditionValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameLocalized).NotEmpty();
        RuleFor(x => x.Category)
            .Must(c => AllowedCategories.Contains(c))
            .WithMessage($"Category must be one of: {string.Join(", ", AllowedCategories)}.");
    }
}
