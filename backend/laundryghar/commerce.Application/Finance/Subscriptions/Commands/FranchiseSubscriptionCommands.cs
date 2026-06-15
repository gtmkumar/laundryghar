using commerce.Application.Common.Interfaces;
using commerce.Application.Finance.Subscriptions.Dtos;
using commerce.Application.Finance.Subscriptions.Queries;
using FluentValidation;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty.Subscriptions;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Finance.Subscriptions.Commands;

/// <summary>
/// Assigns a platform plan to a franchise, creating a new franchise_subscription row
/// and an 'created' event. Only one live subscription per franchise is allowed
/// (enforced by unique partial index on the DB). Writes a franchise_subscription_event.
/// </summary>
public sealed record AssignFranchisePlanCommand(AssignFranchisePlanRequest Request, Guid? ActorId)
    : ICommand<FranchiseSubscriptionDto>;

public sealed class AssignFranchisePlanHandler : ICommandHandler<AssignFranchisePlanCommand, FranchiseSubscriptionDto>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;

    public AssignFranchisePlanHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<FranchiseSubscriptionDto> HandleAsync(AssignFranchisePlanCommand cmd, CancellationToken ct)
    {
        if (!_user.IsPlatformAdmin)
            throw new UnauthorizedAccessException("Only platform administrators may assign SaaS plans to franchises.");

        var req = cmd.Request;

        var franchise = await _db.Franchises
            .FirstOrDefaultAsync(f => f.Id == req.FranchiseId && f.DeletedAt == null, ct)
            ?? throw new InvalidOperationException($"Franchise {req.FranchiseId} not found.");

        var plan = await _db.PlatformPlans
            .FirstOrDefaultAsync(p => p.Id == req.PlatformPlanId && p.Status == "active" && p.DeletedAt == null, ct)
            ?? throw new InvalidOperationException($"Platform plan {req.PlatformPlanId} not found or inactive.");

        // Conflict check: unique partial index will throw on DB level, but fail early
        var existing = await _db.FranchiseSubscriptions.AnyAsync(
            fs => fs.FranchiseId == req.FranchiseId
               && fs.Status != "cancelled" && fs.Status != "expired", ct);
        if (existing)
            throw new InvalidOperationException(
                "Franchise already has an active SaaS subscription. Cancel or wait for it to expire first.");

        var now   = DateTimeOffset.UtcNow;
        var subNo = $"SAS-{now:yyyyMMdd}-{Guid.NewGuid():N[..8].ToUpper()}";

        var subscription = new FranchiseSubscription
        {
            Id                    = Guid.NewGuid(),
            BrandId               = franchise.BrandId,
            FranchiseId           = req.FranchiseId,
            PlatformPlanId        = req.PlatformPlanId,
            SubscriptionNumber    = subNo,
            PriceSnapshot         = plan.Price,
            BillingInterval       = plan.BillingInterval,
            IntervalCount         = plan.IntervalCount,
            CurrencyCode          = plan.CurrencyCode.Trim(),
            MaxStores             = plan.MaxStores,
            MaxWarehouses         = plan.MaxWarehouses,
            MaxUsers              = plan.MaxUsers,
            MaxOrdersPerMonth     = plan.MaxOrdersPerMonth,
            MaxRiders             = plan.MaxRiders,
            Status                = plan.TrialDays > 0 ? "trialing" : "pending",
            AutoRenew             = req.AutoRenew,
            PaymentMethod         = req.PaymentMethod,
            TrialEndsAt           = plan.TrialDays > 0 ? now.AddDays(plan.TrialDays) : null,
            StartedAt             = now,
            CancelAtPeriodEnd     = false,
            DunningAttempts       = 0,
            CurrentPeriodOrders   = 0,
            TotalCyclesBilled     = 0,
            Metadata              = "{}",
            CreatedAt             = now,
            UpdatedAt             = now,
            Version               = 1
        };

        _db.FranchiseSubscriptions.Add(subscription);

        var evt = new FranchiseSubscriptionEvent
        {
            Id                        = Guid.NewGuid(),
            BrandId                   = franchise.BrandId,
            FranchiseSubscriptionId   = subscription.Id,
            FranchiseId               = req.FranchiseId,
            EventType                 = "created",
            ToPlanId                  = req.PlatformPlanId,
            FromStatus                = null,
            ToStatus                  = subscription.Status,
            ActorType                 = "platform_admin",
            ActorId                   = cmd.ActorId,
            Metadata                  = "{}",
            OccurredAt                = now
        };
        _db.FranchiseSubscriptionEvents.Add(evt);

        await _db.SaveChangesAsync(ct);
        return GetFranchiseSubscriptionsHandler.ToDto(subscription);
    }
}

public sealed class AssignFranchisePlanValidator : AbstractValidator<AssignFranchisePlanRequest>
{
    public AssignFranchisePlanValidator()
    {
        RuleFor(x => x.FranchiseId).NotEmpty();
        RuleFor(x => x.PlatformPlanId).NotEmpty();
        RuleFor(x => x.PaymentMethod).NotEmpty()
            .Must(v => v is "invoice" or "auto_debit")
            .WithMessage("payment_method must be one of: invoice, auto_debit");
    }
}

public sealed record CancelFranchiseSubscriptionCommand(Guid SubscriptionId, string? Reason, Guid? ActorId)
    : ICommand<FranchiseSubscriptionDto?>;

public sealed class CancelFranchiseSubscriptionHandler
    : ICommandHandler<CancelFranchiseSubscriptionCommand, FranchiseSubscriptionDto?>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;

    public CancelFranchiseSubscriptionHandler(ICommerceDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<FranchiseSubscriptionDto?> HandleAsync(CancelFranchiseSubscriptionCommand cmd, CancellationToken ct)
    {
        if (!_user.IsPlatformAdmin)
            throw new UnauthorizedAccessException("Only platform administrators may cancel franchise SaaS subscriptions.");

        var sub = await _db.FranchiseSubscriptions
            .FirstOrDefaultAsync(fs => fs.Id == cmd.SubscriptionId, ct);
        if (sub is null) return null;

        if (sub.Status is "cancelled" or "expired")
            throw new InvalidOperationException($"Subscription is already {sub.Status}.");

        var now           = DateTimeOffset.UtcNow;
        var prevStatus    = sub.Status;
        sub.Status        = "cancelled";
        sub.CancelledAt   = now;
        sub.CancelReason  = cmd.Reason;
        sub.CancelAtPeriodEnd = false;
        sub.EndedAt       = now;
        sub.UpdatedAt     = now;
        sub.Version++;

        var evt = new FranchiseSubscriptionEvent
        {
            Id                      = Guid.NewGuid(),
            BrandId                 = sub.BrandId,
            FranchiseSubscriptionId = sub.Id,
            FranchiseId             = sub.FranchiseId,
            EventType               = "cancelled",
            FromStatus              = prevStatus,
            ToStatus                = "cancelled",
            Reason                  = cmd.Reason,
            ActorType               = "platform_admin",
            ActorId                 = cmd.ActorId,
            Metadata                = "{}",
            OccurredAt              = now
        };
        _db.FranchiseSubscriptionEvents.Add(evt);

        await _db.SaveChangesAsync(ct);
        return GetFranchiseSubscriptionsHandler.ToDto(sub);
    }
}
