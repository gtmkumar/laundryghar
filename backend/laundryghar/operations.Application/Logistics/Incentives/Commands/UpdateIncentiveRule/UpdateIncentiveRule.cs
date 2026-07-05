using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Incentives.Commands.CreateIncentiveRule;
using operations.Application.Logistics.Incentives.Dtos;

namespace operations.Application.Logistics.Incentives.Commands.UpdateIncentiveRule;

// ── Update IncentiveRule ──────────────────────────────────────────────────────

public sealed record UpdateIncentiveRuleCommand(Guid Id, UpdateIncentiveRuleRequest Request, Guid? ActorId)
    : ICommand<IncentiveRuleDto?>;

public sealed class UpdateIncentiveRuleHandler
    : ICommandHandler<UpdateIncentiveRuleCommand, IncentiveRuleDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public UpdateIncentiveRuleHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<IncentiveRuleDto?> HandleAsync(UpdateIncentiveRuleCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var rule    = await _db.IncentiveRules
            .FirstOrDefaultAsync(r => r.Id == command.Id && r.BrandId == brandId, cancellationToken);
        if (rule is null) return null;

        var req = command.Request;

        rule.Name         = req.Name;
        rule.RuleType     = req.RuleType;
        rule.Threshold    = req.Threshold;
        rule.RewardAmount = req.RewardAmount;
        rule.ValidUntil   = req.ValidUntil;
        if (req.IsActive is not null) rule.IsActive = req.IsActive.Value;

        rule.UpdatedAt = DateTimeOffset.UtcNow;
        rule.UpdatedBy = command.ActorId;

        await _db.SaveChangesAsync(cancellationToken);
        return CreateIncentiveRuleHandler.ToDto(rule);
    }
}

public sealed class UpdateIncentiveRuleRequestValidator : AbstractValidator<UpdateIncentiveRuleRequest>
{
    public UpdateIncentiveRuleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RuleType)
            .Must(t => t is "trips_target" or "surge_bonus")
            .WithMessage("RuleType must be 'trips_target' or 'surge_bonus'.");
        RuleFor(x => x.Threshold).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RewardAmount).GreaterThanOrEqualTo(0);
    }
}
