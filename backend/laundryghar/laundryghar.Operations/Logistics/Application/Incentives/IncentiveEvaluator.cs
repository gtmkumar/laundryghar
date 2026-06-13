using laundryghar.Logistics.Application.RiderOps;
using laundryghar.Orders.Application.Fare;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.SharedDataModel.Enums;

namespace laundryghar.Logistics.Application.Incentives;

/// <summary>
/// Evaluates active incentive rules when a rider completes a delivery leg and inserts
/// <see cref="RiderIncentiveAward"/> rows. Best-effort: callers wrap this so a failure
/// never blocks task completion. Idempotent via the (rider, rule, period_key) unique index
/// and an explicit existence check.
///
///   trips_target — award once per IST day when the rider's completed deliveries hit the
///                  threshold (period_key = the IST day).
///   surge_bonus  — award once per trip completed within a fare surge window
///                  (period_key = the delivery_assignment id).
/// </summary>
internal static class IncentiveEvaluator
{
    private static readonly TimeSpan IstOffset = TimeSpan.FromHours(5.5);

    internal static async Task EvaluateOnDeliveryAsync(
        LaundryGharDbContext db, DeliveryAssignment da, DateTimeOffset now, CancellationToken ct)
    {
        // Only real deliveries (not the pickup→store leg) earn delivery incentives.
        if (da.LegType is not ("delivery" or "return")) return;

        var brandId = da.BrandId;
        var riderId = da.RiderId;

        var rules = await db.IncentiveRules.AsNoTracking()
            .Where(r => r.BrandId == brandId && r.IsActive
                     && r.ValidFrom <= now
                     && (r.ValidUntil == null || r.ValidUntil >= now))
            .ToListAsync(ct);
        if (rules.Count == 0) return;

        var istDay = DateOnly.FromDateTime(now.ToOffset(IstOffset).Date);
        var dayKey = istDay.ToString("yyyy-MM-dd");
        var (startUtc, endUtc) = RiderOpsTime.IstRangeUtc(istDay, istDay);

        FareSettings? fare = null;
        var added = false;

        foreach (var rule in rules)
        {
            if (rule.RuleType == IncentiveRuleType.TripsTarget)
            {
                if (rule.Threshold <= 0) continue;

                var todayCount = await db.DeliveryAssignments.CountAsync(d =>
                    d.BrandId == brandId && d.RiderId == riderId
                    && d.Status == "completed"
                    && (d.LegType == "delivery" || d.LegType == "return")
                    && d.CompletedAt >= startUtc && d.CompletedAt < endUtc, ct);
                if (todayCount < rule.Threshold) continue;

                if (await AwardExistsAsync(db, riderId, rule.Id, dayKey, ct)) continue;
                AddAward(db, da, rule, rule.RewardAmount, dayKey, now);
                added = true;
            }
            else if (rule.RuleType == IncentiveRuleType.SurgeBonus)
            {
                fare ??= await FareConfig.LoadAsync(db, brandId, ct);
                if (fare.SurgeAt(now) <= 1m) continue;

                var tripKey = da.Id.ToString();
                if (await AwardExistsAsync(db, riderId, rule.Id, tripKey, ct)) continue;
                AddAward(db, da, rule, rule.RewardAmount, tripKey, now);
                added = true;
            }
        }

        if (added) await db.SaveChangesAsync(ct);
    }

    private static Task<bool> AwardExistsAsync(
        LaundryGharDbContext db, Guid riderId, Guid ruleId, string periodKey, CancellationToken ct)
        => db.RiderIncentiveAwards.AnyAsync(
            a => a.RiderId == riderId && a.RuleId == ruleId && a.PeriodKey == periodKey, ct);

    private static void AddAward(
        LaundryGharDbContext db, DeliveryAssignment da, IncentiveRule rule,
        decimal amount, string periodKey, DateTimeOffset now)
        => db.RiderIncentiveAwards.Add(new RiderIncentiveAward
        {
            Id = Guid.NewGuid(),
            RiderId = da.RiderId,
            BrandId = da.BrandId,
            RuleId = rule.Id,
            RuleNameSnapshot = rule.Name,
            RuleType = rule.RuleType,
            Amount = amount,
            PeriodKey = periodKey,
            DeliveryAssignmentId = da.Id,
            AwardedAt = now,
            Metadata = "{}",
            CreatedAt = now,
        });
}
