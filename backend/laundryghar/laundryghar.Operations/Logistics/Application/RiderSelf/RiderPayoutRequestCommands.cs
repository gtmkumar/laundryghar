using FluentValidation;
using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using MediatR;

namespace laundryghar.Logistics.Application.RiderSelf;

// ── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>A rider's withdrawable-balance breakdown.</summary>
public sealed record RiderBalanceDto(
    decimal EarnedPayout,
    decimal Incentives,
    decimal WithdrawnOrPending,
    decimal Available);

public sealed record RiderPayoutRequestDto(
    Guid Id,
    decimal Amount,
    string Status,
    string? RejectionReason,
    string? PaymentReference,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset? PaidAt);

public sealed record RiderIncentiveAwardDto(
    Guid Id,
    string RuleName,
    string RuleType,
    decimal Amount,
    DateTimeOffset AwardedAt);

/// <summary>Rider-self withdrawal request body.</summary>
public sealed record RequestPayoutBody(decimal Amount);

// ── Shared balance helper ────────────────────────────────────────────────────

internal static class RiderBalance
{
    /// <summary>available = earned payouts + incentive awards − (approved+paid+pending requests).</summary>
    internal static async Task<RiderBalanceDto> ComputeAsync(
        LaundryGharDbContext db, Guid riderId, Guid brandId, CancellationToken ct)
    {
        var earned = await db.DeliveryAssignments.AsNoTracking()
            .Where(d => d.BrandId == brandId && d.RiderId == riderId
                     && d.Status == "completed" && d.PayoutAmount != null)
            .SumAsync(d => (decimal?)d.PayoutAmount, ct) ?? 0m;

        var incentives = await db.RiderIncentiveAwards.AsNoTracking()
            .Where(a => a.BrandId == brandId && a.RiderId == riderId)
            .SumAsync(a => (decimal?)a.Amount, ct) ?? 0m;

        // Anything not rejected reduces the available balance.
        var committed = await db.RiderPayoutRequests.AsNoTracking()
            .Where(p => p.BrandId == brandId && p.RiderId == riderId
                     && p.Status != RiderPayoutRequestStatus.Rejected)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        var available = earned + incentives - committed;
        if (available < 0m) available = 0m;
        return new RiderBalanceDto(earned, incentives, committed, available);
    }
}

// ── Rider: balance ───────────────────────────────────────────────────────────

public sealed record GetMyBalanceQuery(Guid UserId, Guid BrandId) : IRequest<RiderBalanceDto?>;

public sealed class GetMyBalanceHandler : IRequestHandler<GetMyBalanceQuery, RiderBalanceDto?>
{
    private readonly LaundryGharDbContext _db;
    public GetMyBalanceHandler(LaundryGharDbContext db) => _db = db;

    public async Task<RiderBalanceDto?> Handle(GetMyBalanceQuery q, CancellationToken ct)
    {
        var rider = await _db.Riders.AsNoTracking()
            .Where(r => r.UserId == q.UserId && r.BrandId == q.BrandId)
            .Select(r => new { r.Id }).FirstOrDefaultAsync(ct);
        if (rider is null) return null;
        return await RiderBalance.ComputeAsync(_db, rider.Id, q.BrandId, ct);
    }
}

// ── Rider: request a withdrawal ──────────────────────────────────────────────

public sealed record RequestPayoutCommand(Guid UserId, Guid BrandId, decimal Amount)
    : IRequest<RiderPayoutRequestDto>;

public sealed class RequestPayoutHandler : IRequestHandler<RequestPayoutCommand, RiderPayoutRequestDto>
{
    private readonly LaundryGharDbContext _db;
    public RequestPayoutHandler(LaundryGharDbContext db) => _db = db;

    public async Task<RiderPayoutRequestDto> Handle(RequestPayoutCommand cmd, CancellationToken ct)
    {
        var rider = await _db.Riders
            .FirstOrDefaultAsync(r => r.UserId == cmd.UserId && r.BrandId == cmd.BrandId, ct)
            ?? throw new KeyNotFoundException("Rider profile not found.");

        var balance = await RiderBalance.ComputeAsync(_db, rider.Id, cmd.BrandId, ct);
        if (cmd.Amount > balance.Available)
            throw new BusinessRuleException(
                $"Requested amount ({cmd.Amount:0.00}) exceeds available balance ({balance.Available:0.00}).");

        var now = DateTimeOffset.UtcNow;
        var req = new RiderPayoutRequest
        {
            Id = Guid.NewGuid(),
            RiderId = rider.Id,
            BrandId = cmd.BrandId,
            FranchiseId = rider.FranchiseId,
            StoreId = rider.PrimaryStoreId,
            Amount = cmd.Amount,
            Status = RiderPayoutRequestStatus.Requested,
            RequestedAt = now,
            Metadata = "{}",
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = cmd.UserId,
            UpdatedBy = cmd.UserId,
        };
        _db.RiderPayoutRequests.Add(req);
        await _db.SaveChangesAsync(ct);
        return ToDto(req);
    }

    internal static RiderPayoutRequestDto ToDto(RiderPayoutRequest p) =>
        new(p.Id, p.Amount, p.Status, p.RejectionReason, p.PaymentReference,
            p.RequestedAt, p.ReviewedAt, p.PaidAt);
}

public sealed class RequestPayoutValidator : AbstractValidator<RequestPayoutCommand>
{
    public RequestPayoutValidator() => RuleFor(x => x.Amount).GreaterThan(0m);
}

// ── Rider: my payout requests + my incentive awards ──────────────────────────

public sealed record GetMyPayoutRequestsQuery(Guid UserId, Guid BrandId)
    : IRequest<IReadOnlyList<RiderPayoutRequestDto>>;

public sealed class GetMyPayoutRequestsHandler
    : IRequestHandler<GetMyPayoutRequestsQuery, IReadOnlyList<RiderPayoutRequestDto>>
{
    private readonly LaundryGharDbContext _db;
    public GetMyPayoutRequestsHandler(LaundryGharDbContext db) => _db = db;

    public async Task<IReadOnlyList<RiderPayoutRequestDto>> Handle(GetMyPayoutRequestsQuery q, CancellationToken ct)
    {
        var rider = await _db.Riders.AsNoTracking()
            .Where(r => r.UserId == q.UserId && r.BrandId == q.BrandId)
            .Select(r => new { r.Id }).FirstOrDefaultAsync(ct);
        if (rider is null) return [];

        return await _db.RiderPayoutRequests.AsNoTracking()
            .Where(p => p.RiderId == rider.Id && p.BrandId == q.BrandId)
            .OrderByDescending(p => p.RequestedAt)
            .Select(p => new RiderPayoutRequestDto(
                p.Id, p.Amount, p.Status, p.RejectionReason, p.PaymentReference,
                p.RequestedAt, p.ReviewedAt, p.PaidAt))
            .ToListAsync(ct);
    }
}

public sealed record GetMyIncentivesQuery(Guid UserId, Guid BrandId, int Days)
    : IRequest<IReadOnlyList<RiderIncentiveAwardDto>>;

public sealed class GetMyIncentivesHandler
    : IRequestHandler<GetMyIncentivesQuery, IReadOnlyList<RiderIncentiveAwardDto>>
{
    private readonly LaundryGharDbContext _db;
    public GetMyIncentivesHandler(LaundryGharDbContext db) => _db = db;

    public async Task<IReadOnlyList<RiderIncentiveAwardDto>> Handle(GetMyIncentivesQuery q, CancellationToken ct)
    {
        var rider = await _db.Riders.AsNoTracking()
            .Where(r => r.UserId == q.UserId && r.BrandId == q.BrandId)
            .Select(r => new { r.Id }).FirstOrDefaultAsync(ct);
        if (rider is null) return [];

        var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(q.Days, 1, 90));
        return await _db.RiderIncentiveAwards.AsNoTracking()
            .Where(a => a.RiderId == rider.Id && a.BrandId == q.BrandId && a.AwardedAt >= since)
            .OrderByDescending(a => a.AwardedAt)
            .Select(a => new RiderIncentiveAwardDto(a.Id, a.RuleNameSnapshot, a.RuleType, a.Amount, a.AwardedAt))
            .ToListAsync(ct);
    }
}
