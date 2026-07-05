using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.Utilities.Services;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Incentives.Dtos;

namespace operations.Application.Logistics.Incentives.Commands.CreateIncentiveRule;

// ── Create IncentiveRule ──────────────────────────────────────────────────────

public sealed record CreateIncentiveRuleCommand(CreateIncentiveRuleRequest Request, Guid? ActorId)
    : ICommand<IncentiveRuleDto>;

public sealed class CreateIncentiveRuleHandler
    : ICommandHandler<CreateIncentiveRuleCommand, IncentiveRuleDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public CreateIncentiveRuleHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<IncentiveRuleDto> HandleAsync(CreateIncentiveRuleCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var req     = command.Request;
        var now     = DateTimeOffset.UtcNow;

        var rule = new IncentiveRule
        {
            Id           = Guid.NewGuid(),
            BrandId      = brandId,
            Name         = req.Name,
            RuleType     = req.RuleType,
            Threshold    = req.Threshold,
            RewardAmount = req.RewardAmount,
            Window       = "daily",
            IsActive     = req.IsActive ?? true,
            ValidFrom    = now,
            ValidUntil   = req.ValidUntil,
            Metadata     = "{}",
            CreatedAt    = now,
            UpdatedAt    = now,
            CreatedBy    = command.ActorId,
            UpdatedBy    = command.ActorId
        };

        _db.IncentiveRules.Add(rule);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(rule);
    }

    internal static IncentiveRuleDto ToDto(IncentiveRule r) => new(
        r.Id, r.Name, r.RuleType, r.Threshold, r.RewardAmount,
        r.Window, r.IsActive, r.ValidFrom, r.ValidUntil);
}

public sealed class CreateIncentiveRuleRequestValidator : AbstractValidator<CreateIncentiveRuleRequest>
{
    public CreateIncentiveRuleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RuleType)
            .Must(t => t is "trips_target" or "surge_bonus")
            .WithMessage("RuleType must be 'trips_target' or 'surge_bonus'.");
        RuleFor(x => x.Threshold).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RewardAmount).GreaterThanOrEqualTo(0);
    }
}
