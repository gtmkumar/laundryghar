using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Logistics.Riders.Commands.PayoutAdmin;

// ── DTO ──────────────────────────────────────────────────────────────────────

/// <summary>Admin mark-paid request body (optional payment reference).</summary>
public sealed record MarkPayoutPaidRequest(string? Reference);

public sealed record PayoutRequestAdminDto(
    Guid Id,
    Guid RiderId,
    string? RiderName,
    decimal Amount,
    string Status,
    string? RejectionReason,
    string? PaymentReference,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset? PaidAt);

// ── Queue ────────────────────────────────────────────────────────────────────

public sealed record GetPayoutRequestsQuery(string? Status) : IQuery<IReadOnlyList<PayoutRequestAdminDto>>;

public sealed class GetPayoutRequestsHandler : IQueryHandler<GetPayoutRequestsQuery, IReadOnlyList<PayoutRequestAdminDto>>
{
    private readonly IOperationsDbContext _db;
    public GetPayoutRequestsHandler(IOperationsDbContext db) => _db = db;

    public async Task<IReadOnlyList<PayoutRequestAdminDto>> HandleAsync(GetPayoutRequestsQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var rows = await _db.RiderPayoutRequests.AsNoTracking()
            .Where(p => query.Status == null || p.Status == query.Status)
            .OrderByDescending(p => p.RequestedAt)
            .Select(p => new { p.Id, p.RiderId, p.Amount, p.Status, p.RejectionReason,
                               p.PaymentReference, p.RequestedAt, p.ReviewedAt, p.PaidAt })
            .ToListAsync(ct);

        var riderIds = rows.Select(r => r.RiderId).Distinct().ToList();
        var userIdByRider = await _db.Riders.AsNoTracking()
            .Where(r => riderIds.Contains(r.Id))
            .Select(r => new { r.Id, r.UserId }).ToListAsync(ct);
        var userIds = userIdByRider.Select(x => x.UserId).Distinct().ToList();
        var names = await _db.UserProfiles.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, Name = ((p.FirstName ?? "") + " " + (p.LastName ?? "")).Trim() })
            .ToListAsync(ct);
        string? NameFor(Guid riderId)
        {
            var uid = userIdByRider.FirstOrDefault(x => x.Id == riderId)?.UserId;
            var n = uid is null ? null : names.FirstOrDefault(x => x.UserId == uid)?.Name;
            return string.IsNullOrWhiteSpace(n) ? null : n;
        }

        return rows.Select(p => new PayoutRequestAdminDto(
            p.Id, p.RiderId, NameFor(p.RiderId), p.Amount, p.Status, p.RejectionReason,
            p.PaymentReference, p.RequestedAt, p.ReviewedAt, p.PaidAt)).ToList();
    }
}

// ── Approve / Reject / Mark-paid ─────────────────────────────────────────────

public sealed record ReviewPayoutRequestCommand(Guid RequestId, bool Approve, string? Reason, Guid? ActorId)
    : ICommand<PayoutRequestAdminDto?>;

public sealed class ReviewPayoutRequestHandler : ICommandHandler<ReviewPayoutRequestCommand, PayoutRequestAdminDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public ReviewPayoutRequestHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<PayoutRequestAdminDto?> HandleAsync(ReviewPayoutRequestCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var req = await _db.RiderPayoutRequests.FirstOrDefaultAsync(p => p.Id == command.RequestId, ct);
        if (req is null) return null;
        if (!_user.IsWithinScope(brandId: req.BrandId, franchiseId: req.FranchiseId, storeId: req.StoreId))
            throw new ForbiddenException("This payout request is outside your assigned scope.");
        if (req.Status != RiderPayoutRequestStatus.Requested)
            throw new BusinessRuleException($"Only a 'requested' payout can be reviewed (current: {req.Status}).");

        var now = DateTimeOffset.UtcNow;
        req.Status = command.Approve ? RiderPayoutRequestStatus.Approved : RiderPayoutRequestStatus.Rejected;
        req.RejectionReason = command.Approve ? null : command.Reason;
        req.ReviewedBy = command.ActorId;
        req.ReviewedAt = now;
        req.UpdatedAt = now;
        req.UpdatedBy = command.ActorId;
        await _db.SaveChangesAsync(ct);
        return ToDto(req, null);
    }

    internal static PayoutRequestAdminDto ToDto(RiderPayoutRequest p, string? name) =>
        new(p.Id, p.RiderId, name, p.Amount, p.Status, p.RejectionReason, p.PaymentReference,
            p.RequestedAt, p.ReviewedAt, p.PaidAt);
}

/// <summary>Marks an approved payout as paid and posts a cash_out entry to the store's cash book.</summary>
public sealed record MarkPayoutPaidCommand(Guid RequestId, string? Reference, Guid? ActorId)
    : ICommand<PayoutRequestAdminDto?>;

public sealed class MarkPayoutPaidHandler : ICommandHandler<MarkPayoutPaidCommand, PayoutRequestAdminDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public MarkPayoutPaidHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<PayoutRequestAdminDto?> HandleAsync(MarkPayoutPaidCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var req = await _db.RiderPayoutRequests.FirstOrDefaultAsync(p => p.Id == command.RequestId, ct);
        if (req is null) return null;
        if (!_user.IsWithinScope(brandId: req.BrandId, franchiseId: req.FranchiseId, storeId: req.StoreId))
            throw new ForbiddenException("This payout request is outside your assigned scope.");
        if (req.Status != RiderPayoutRequestStatus.Approved)
            throw new BusinessRuleException($"Only an 'approved' payout can be marked paid (current: {req.Status}).");

        var now = DateTimeOffset.UtcNow;
        req.Status = RiderPayoutRequestStatus.Paid;
        req.PaymentReference = command.Reference;
        req.PaidBy = command.ActorId;
        req.PaidAt = now;
        req.UpdatedAt = now;
        req.UpdatedBy = command.ActorId;

        await PostCashOutAsync(req, now, command.ActorId, ct);
        await _db.SaveChangesAsync(ct);
        return ReviewPayoutRequestHandler.ToDto(req, null);
    }

    // Mirrors the COD-settlement cash-book posting, but cash_out / salary / rider_payout.
    private async Task PostCashOutAsync(RiderPayoutRequest req, DateTimeOffset now, Guid? actorId, CancellationToken ct)
    {
        if (req.StoreId is not Guid storeId) return;   // no store → nothing to post against
        var bookDate = DateOnly.FromDateTime(now.UtcDateTime);

        var book = await _db.CashBooks.FirstOrDefaultAsync(
            b => b.BrandId == req.BrandId && b.StoreId == storeId
              && b.BookDate == bookDate && b.ShiftLabel == "full_day", ct);

        if (book is null)
        {
            book = new CashBook
            {
                Id = Guid.NewGuid(), BrandId = req.BrandId, FranchiseId = req.FranchiseId ?? Guid.Empty,
                StoreId = storeId, BookDate = bookDate, ShiftLabel = "full_day",
                OpeningUserId = actorId ?? Guid.Empty, OpeningBalance = 0, CashInflow = 0, CashOutflow = 0,
                UpiInflow = 0, CardInflow = 0, OtherInflow = 0, DepositAmount = 0,
                TotalOrders = 0, NewOrders = 0, DeliveredOrders = 0, CancelledOrders = 0,
                Status = "open", Metadata = "{}", OpenedAt = now, CreatedAt = now, UpdatedAt = now,
                CreatedBy = actorId, UpdatedBy = actorId,
            };
            _db.CashBooks.Add(book);
        }
        else if (book.Status != "open")
        {
            return;   // book closed → payout stands alone (no posting)
        }

        _db.CashBookEntries.Add(new CashBookEntry
        {
            Id = Guid.NewGuid(), CashBookId = book.Id, BrandId = req.BrandId, StoreId = storeId,
            EntryType = "cash_out", Category = "salary", Direction = -1, Amount = req.Amount,
            PaymentMode = "cash", ReferenceType = "rider_payout", ReferenceId = req.Id,
            Description = $"Rider payout{(string.IsNullOrWhiteSpace(req.PaymentReference) ? "" : $" ({req.PaymentReference})")}",
            PerformedBy = actorId ?? Guid.Empty, OccurredAt = now, Metadata = "{}",
            CreatedAt = now, CreatedBy = actorId,
        });

        book.CashOutflow += req.Amount;
        book.UpdatedAt = now;
        book.UpdatedBy = actorId;
    }
}
