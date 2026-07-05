using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Incentives.Commands.CreateIncentiveRule;
using operations.Application.Logistics.Incentives.Dtos;

namespace operations.Application.Logistics.Incentives.Queries.GetIncentiveRules;

// ── List IncentiveRules (bare array, brand-scoped) ────────────────────────────

/// <summary>When <paramref name="ActiveOnly"/> is true, returns only active rules that
/// have not expired (ValidUntil null or in the future).</summary>
public sealed record GetIncentiveRulesQuery(bool ActiveOnly)
    : IQuery<IReadOnlyList<IncentiveRuleDto>>;

public sealed class GetIncentiveRulesHandler
    : IQueryHandler<GetIncentiveRulesQuery, IReadOnlyList<IncentiveRuleDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetIncentiveRulesHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<IReadOnlyList<IncentiveRuleDto>> HandleAsync(GetIncentiveRulesQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var q       = _db.IncentiveRules.Where(r => r.BrandId == brandId);

        if (query.ActiveOnly)
        {
            var now = DateTimeOffset.UtcNow;
            q = q.Where(r => r.IsActive && (r.ValidUntil == null || r.ValidUntil >= now));
        }

        return await q
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => CreateIncentiveRuleHandler.ToDto(r))
            .ToListAsync(cancellationToken);
    }
}
