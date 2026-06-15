using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.RiderSelf.Dtos;

namespace operations.Application.Logistics.RiderSelf.Commands.RiderPayoutRequests;

// ── Shared balance helper ────────────────────────────────────────────────────

internal static class RiderBalance
{
    /// <summary>available = earned payouts + incentive awards − (approved+paid+pending requests).</summary>
    internal static async Task<RiderBalanceDto> ComputeAsync(
        IOperationsDbContext db, Guid riderId, Guid brandId, CancellationToken ct)
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

public sealed record GetMyBalanceQuery(Guid UserId, Guid BrandId) : IQuery<RiderBalanceDto?>;

public sealed class GetMyBalanceHandler : IQueryHandler<GetMyBalanceQuery, RiderBalanceDto?>
{
    private readonly IOperationsDbContext _db;
    public GetMyBalanceHandler(IOperationsDbContext db) => _db = db;

    public async Task<RiderBalanceDto?> HandleAsync(GetMyBalanceQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var rider = await _db.Riders.AsNoTracking()
            .Where(r => r.UserId == query.UserId && r.BrandId == query.BrandId)
            .Select(r => new { r.Id }).FirstOrDefaultAsync(ct);
        if (rider is null) return null;
        return await RiderBalance.ComputeAsync(_db, rider.Id, query.BrandId, ct);
    }
}

// ── Rider: request a withdrawal ──────────────────────────────────────────────

public sealed record RequestPayoutCommand(Guid UserId, Guid BrandId, decimal Amount)
    : ICommand<RiderPayoutRequestDto>;

public sealed class RequestPayoutHandler : ICommandHandler<RequestPayoutCommand, RiderPayoutRequestDto>
{
    private readonly IOperationsDbContext _db;
    public RequestPayoutHandler(IOperationsDbContext db) => _db = db;

    public async Task<RiderPayoutRequestDto> HandleAsync(RequestPayoutCommand command, CancellationToken cancellationToken)
    {
        var ct  = cancellationToken;
        var cmd = command;
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
    : IQuery<IReadOnlyList<RiderPayoutRequestDto>>;

public sealed class GetMyPayoutRequestsHandler
    : IQueryHandler<GetMyPayoutRequestsQuery, IReadOnlyList<RiderPayoutRequestDto>>
{
    private readonly IOperationsDbContext _db;
    public GetMyPayoutRequestsHandler(IOperationsDbContext db) => _db = db;

    public async Task<IReadOnlyList<RiderPayoutRequestDto>> HandleAsync(GetMyPayoutRequestsQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var rider = await _db.Riders.AsNoTracking()
            .Where(r => r.UserId == query.UserId && r.BrandId == query.BrandId)
            .Select(r => new { r.Id }).FirstOrDefaultAsync(ct);
        if (rider is null) return [];

        return await _db.RiderPayoutRequests.AsNoTracking()
            .Where(p => p.RiderId == rider.Id && p.BrandId == query.BrandId)
            .OrderByDescending(p => p.RequestedAt)
            .Select(p => new RiderPayoutRequestDto(
                p.Id, p.Amount, p.Status, p.RejectionReason, p.PaymentReference,
                p.RequestedAt, p.ReviewedAt, p.PaidAt))
            .ToListAsync(ct);
    }
}

public sealed record GetMyIncentivesQuery(Guid UserId, Guid BrandId, int Days)
    : IQuery<IReadOnlyList<RiderIncentiveAwardDto>>;

public sealed class GetMyIncentivesHandler
    : IQueryHandler<GetMyIncentivesQuery, IReadOnlyList<RiderIncentiveAwardDto>>
{
    private readonly IOperationsDbContext _db;
    public GetMyIncentivesHandler(IOperationsDbContext db) => _db = db;

    public async Task<IReadOnlyList<RiderIncentiveAwardDto>> HandleAsync(GetMyIncentivesQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var rider = await _db.Riders.AsNoTracking()
            .Where(r => r.UserId == query.UserId && r.BrandId == query.BrandId)
            .Select(r => new { r.Id }).FirstOrDefaultAsync(ct);
        if (rider is null) return [];

        var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(query.Days, 1, 90));
        return await _db.RiderIncentiveAwards.AsNoTracking()
            .Where(a => a.RiderId == rider.Id && a.BrandId == query.BrandId && a.AwardedAt >= since)
            .OrderByDescending(a => a.AwardedAt)
            .Select(a => new RiderIncentiveAwardDto(a.Id, a.RuleNameSnapshot, a.RuleType, a.Amount, a.AwardedAt))
            .ToListAsync(ct);
    }
}
