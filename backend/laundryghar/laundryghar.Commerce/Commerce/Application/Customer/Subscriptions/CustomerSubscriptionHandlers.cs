using FluentValidation;
using laundryghar.Commerce.Application;
using laundryghar.Commerce.Application.Admin.Subscriptions;
using laundryghar.Commerce.Infrastructure.Gateway;
using laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;
using MediatR;

namespace laundryghar.Commerce.Application.Customer.Subscriptions;

// ── Query: available plans ────────────────────────────────────────────────────

public sealed record GetActiveSubscriptionPlansQuery(Guid CustomerId, Guid BrandId)
    : IRequest<List<SubscriptionPlanDto>>;

public sealed class GetActiveSubscriptionPlansHandler
    : IRequestHandler<GetActiveSubscriptionPlansQuery, List<SubscriptionPlanDto>>
{
    private readonly LaundryGharDbContext _db;

    public GetActiveSubscriptionPlansHandler(LaundryGharDbContext db) => _db = db;

    public Task<List<SubscriptionPlanDto>> Handle(GetActiveSubscriptionPlansQuery q, CancellationToken ct)
        => _db.SubscriptionPlans
            .Where(p => p.BrandId == q.BrandId
                     && p.Status == "active"
                     && p.IsPublic
                     && p.DeletedAt == null
                     && (p.AvailableFrom == null || p.AvailableFrom <= DateTimeOffset.UtcNow)
                     && (p.AvailableTo   == null || p.AvailableTo   >= DateTimeOffset.UtcNow))
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Price)
            .Select(p => GetSubscriptionPlansHandler.ToDto(p))
            .ToListAsync(ct);
}

// ── Query: my subscriptions ───────────────────────────────────────────────────

public sealed record GetMySubscriptionsQuery(Guid CustomerId, Guid BrandId)
    : IRequest<List<CustomerSubscriptionDto>>;

public sealed class GetMySubscriptionsHandler
    : IRequestHandler<GetMySubscriptionsQuery, List<CustomerSubscriptionDto>>
{
    private readonly LaundryGharDbContext _db;

    public GetMySubscriptionsHandler(LaundryGharDbContext db) => _db = db;

    public Task<List<CustomerSubscriptionDto>> Handle(GetMySubscriptionsQuery q, CancellationToken ct)
        => _db.CustomerSubscriptions
            .Where(cs => cs.CustomerId == q.CustomerId && cs.BrandId == q.BrandId)
            .OrderByDescending(cs => cs.CreatedAt)
            .Select(cs => GetCustomerSubscriptionsAdminHandler.ToDto(cs))
            .ToListAsync(ct);
}

// ── Command: subscribe ────────────────────────────────────────────────────────

/// <summary>
/// Creates a customer_subscription (status=pending) + payment_mandate (status=created)
/// and calls CreateMandateAsync on the gateway. The subscription activates when the
/// mandate webhook signals 'active' (handled separately via webhook or polling).
/// End-of-period cancellation semantics: CancelAtPeriodEnd is set, not immediate stop.
/// </summary>
public sealed record SubscribeCommand(Guid CustomerId, Guid BrandId, SubscribeRequest Request)
    : IRequest<CustomerSubscriptionDto>;

public sealed class SubscribeHandler : IRequestHandler<SubscribeCommand, CustomerSubscriptionDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly IPaymentGateway      _gateway;
    private readonly ILogger<SubscribeHandler> _logger;

    public SubscribeHandler(LaundryGharDbContext db, IPaymentGateway gateway, ILogger<SubscribeHandler> logger)
    {
        _db      = db;
        _gateway = gateway;
        _logger  = logger;
    }

    public async Task<CustomerSubscriptionDto> Handle(SubscribeCommand cmd, CancellationToken ct)
    {
        var plan = await _db.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.Id == cmd.Request.PlanId
                                   && p.BrandId == cmd.BrandId
                                   && p.Status == "active"
                                   && p.DeletedAt == null, ct)
            ?? throw new InvalidOperationException($"Subscription plan {cmd.Request.PlanId} not found or inactive.");

        // Enforce subscriber cap
        if (plan.MaxActiveSubscribers.HasValue && plan.CurrentSubscriberCount >= plan.MaxActiveSubscribers)
            throw new InvalidOperationException("This subscription plan has reached its maximum subscriber limit.");

        // Check no duplicate active subscription for this customer + plan
        var existing = await _db.CustomerSubscriptions.AnyAsync(
            cs => cs.CustomerId == cmd.CustomerId
               && cs.PlanId     == plan.Id
               && cs.Status     != "cancelled"
               && cs.Status     != "expired", ct);
        if (existing)
            throw new InvalidOperationException("Customer already has an active subscription to this plan.");

        var now            = DateTimeOffset.UtcNow;
        // Compact deterministic format: e.g. SUB-20260610-A1B2C3D4
        var subscriptionNo = $"SUB-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

        // 1. Create payment mandate entity
        var mandate = new PaymentMandate
        {
            Id                = Guid.NewGuid(),
            BrandId           = cmd.BrandId,
            CustomerId        = cmd.CustomerId,
            MandateType       = cmd.Request.MandateType,
            Gateway           = plan.Gateway ?? "razorpay",
            GatewayCustomerId = cmd.Request.GatewayCustomerId,
            MaxAmount         = cmd.Request.MaxMandateAmount,
            DebitFrequency    = plan.BillingInterval == "monthly" ? "monthly" : "as_presented",
            UpiVpa            = cmd.Request.UpiVpa,
            Status            = "created",
            Metadata          = "{}",
            CreatedAt         = now,
            UpdatedAt         = now
        };

        // 2. Call gateway to create mandate (fail-closed; exception bubbles)
        GatewayMandateResult gatewayResult;
        try
        {
            gatewayResult = await _gateway.CreateMandateAsync(new CreateMandateRequest(
                MandateType:       cmd.Request.MandateType,
                GatewayCustomerId: cmd.Request.GatewayCustomerId,
                MaxAmount:         cmd.Request.MaxMandateAmount,
                Currency:          plan.CurrencyCode.Trim(),
                DebitFrequency:    mandate.DebitFrequency,
                UpiVpa:            cmd.Request.UpiVpa,
                Receipt:           subscriptionNo,
                Description:       $"LaundryGhar subscription: {plan.Name}"
            ), ct);

            mandate.GatewayMandateId = gatewayResult.GatewayMandateId;
            mandate.Status           = gatewayResult.Status; // created → pending → active
            mandate.GatewayResponse  = gatewayResult.RawResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gateway CreateMandate failed for customerId={CustomerId} planId={PlanId}",
                cmd.CustomerId, plan.Id);
            throw;
        }

        // 3. Create subscription entity (pending until mandate becomes active)
        var subscription = new CustomerSubscription
        {
            Id                   = Guid.NewGuid(),
            BrandId              = cmd.BrandId,
            CustomerId           = cmd.CustomerId,
            PlanId               = plan.Id,
            SubscriptionNumber   = subscriptionNo,
            PriceSnapshot        = plan.Price,
            BillingInterval      = plan.BillingInterval,
            IntervalCount        = plan.IntervalCount,
            QuotaType            = plan.QuotaType,
            QuotaValue           = plan.QuotaValue,
            CurrencyCode         = plan.CurrencyCode.Trim(),
            Status               = plan.TrialDays > 0 ? "trialing" : "pending",
            AutoRenew            = true,
            CreditsRemaining     = 0,
            TrialEndsAt          = plan.TrialDays > 0 ? now.AddDays(plan.TrialDays) : null,
            StartedAt            = now,
            CancelAtPeriodEnd    = false,
            DunningAttempts      = 0,
            FailedPaymentCount   = 0,
            TotalCyclesBilled    = 0,
            Metadata             = "{}",
            CreatedAt            = now,
            UpdatedAt            = now,
            Version              = 1
        };

        // 4. Atomically save mandate + subscription
        _db.PaymentMandates.Add(mandate);
        _db.CustomerSubscriptions.Add(subscription);

        // Link mandate after add so EF resolves the FK correctly
        subscription.MandateId = mandate.Id;

        // Increment subscriber count on the plan
        plan.CurrentSubscriberCount++;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Subscribe: customerId={CustomerId} planId={PlanId} subscriptionId={SubId} mandateId={MandateId}",
            cmd.CustomerId, plan.Id, subscription.Id, mandate.Id);

        return GetCustomerSubscriptionsAdminHandler.ToDto(subscription);
    }
}

public sealed class SubscribeCommandValidator : AbstractValidator<SubscribeCommand>
{
    public SubscribeCommandValidator()
    {
        RuleFor(x => x.Request.PlanId).NotEmpty();
        RuleFor(x => x.Request.MandateType).NotEmpty()
            .Must(v => v is "upi_autopay" or "emandate" or "card" or "nach")
            .WithMessage("mandate_type must be one of: upi_autopay, emandate, card, nach");
        RuleFor(x => x.Request.MaxMandateAmount).GreaterThan(0);
        RuleFor(x => x.Request.UpiVpa)
            .NotEmpty()
            .When(x => x.Request.MandateType == "upi_autopay")
            .WithMessage("upi_vpa is required for upi_autopay mandate type.");
    }
}

// ── Command: cancel ───────────────────────────────────────────────────────────

/// <summary>
/// Sets CancelAtPeriodEnd = true. The subscription continues until current_period_end,
/// then the worker marks it cancelled. Immediate cancellation is not supported per ADR-010.
/// </summary>
public sealed record CancelSubscriptionCommand(Guid SubscriptionId, Guid CustomerId, Guid BrandId, string? Reason)
    : IRequest<CustomerSubscriptionDto?>;

public sealed class CancelSubscriptionHandler : IRequestHandler<CancelSubscriptionCommand, CustomerSubscriptionDto?>
{
    private readonly LaundryGharDbContext _db;

    public CancelSubscriptionHandler(LaundryGharDbContext db) => _db = db;

    public async Task<CustomerSubscriptionDto?> Handle(CancelSubscriptionCommand cmd, CancellationToken ct)
    {
        var sub = await _db.CustomerSubscriptions
            .FirstOrDefaultAsync(cs => cs.Id         == cmd.SubscriptionId
                                    && cs.CustomerId == cmd.CustomerId
                                    && cs.BrandId    == cmd.BrandId, ct);
        if (sub is null) return null;

        if (sub.Status is "cancelled" or "expired")
            throw new InvalidOperationException(
                $"Subscription is already {sub.Status} and cannot be cancelled again.");

        sub.CancelAtPeriodEnd = true;
        sub.CancelReason      = cmd.Reason;
        sub.UpdatedAt         = DateTimeOffset.UtcNow;
        sub.Version++;

        await _db.SaveChangesAsync(ct);
        return GetCustomerSubscriptionsAdminHandler.ToDto(sub);
    }
}
