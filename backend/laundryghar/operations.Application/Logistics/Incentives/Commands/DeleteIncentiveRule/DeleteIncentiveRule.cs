using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Logistics.Incentives.Commands.DeleteIncentiveRule;

// ── Delete IncentiveRule (soft: deactivate) ───────────────────────────────────

/// <summary>Soft-delete: incentive_rules has no DeletedAt column, so we deactivate
/// (IsActive=false) rather than remove the row — awards reference it via RuleId.</summary>
public sealed record DeleteIncentiveRuleCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public sealed class DeleteIncentiveRuleHandler : ICommandHandler<DeleteIncentiveRuleCommand, bool>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public DeleteIncentiveRuleHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> HandleAsync(DeleteIncentiveRuleCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var rule    = await _db.IncentiveRules
            .FirstOrDefaultAsync(r => r.Id == command.Id && r.BrandId == brandId, cancellationToken);
        if (rule is null) return false;

        rule.IsActive  = false;
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        rule.UpdatedBy = command.ActorId;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
