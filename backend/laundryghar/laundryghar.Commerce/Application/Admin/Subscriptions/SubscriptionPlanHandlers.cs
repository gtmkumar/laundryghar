using System.Text.Json;
using FluentValidation;
using laundryghar.Commerce.Application;
using laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Commerce.Application.Admin.Subscriptions;

// ── Queries ───────────────────────────────────────────────────────────────────

public sealed record GetSubscriptionPlansQuery(int Page, int PageSize) : IRequest<PaginatedList<SubscriptionPlanDto>>;

public sealed class GetSubscriptionPlansHandler : IRequestHandler<GetSubscriptionPlansQuery, PaginatedList<SubscriptionPlanDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;

    public GetSubscriptionPlansHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<SubscriptionPlanDto>> Handle(GetSubscriptionPlansQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        return PaginatedList<SubscriptionPlanDto>.CreateAsync(
            _db.SubscriptionPlans
                .Where(x => x.BrandId == brandId && x.DeletedAt == null)
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
                .Select(x => ToDto(x)),
            q.Page, q.PageSize, ct);
    }

    internal static SubscriptionPlanDto ToDto(SubscriptionPlan x) => new(
        x.Id, x.BrandId, x.Code, x.Name, x.NameLocalized, x.Description,
        x.Tier, x.BillingInterval, x.IntervalCount, x.Price, x.SetupFee,
        x.CurrencyCode, x.TrialDays, x.QuotaType, x.QuotaValue,
        x.RolloverUnused, x.MaxRollover, x.OverageDiscountPercent,
        x.ApplicableServices, x.ExcludedServices,
        x.PickupIncluded, x.DeliveryIncluded, x.ExpressIncluded,
        x.MaxActiveSubscribers, x.CurrentSubscriberCount,
        x.Gateway, x.GatewayPlanId, x.TermsAndConditions,
        x.IconUrl, x.ColorHex, x.DisplayOrder, x.IsPublic, x.IsFeatured,
        x.Status, x.AvailableFrom, x.AvailableTo, x.CreatedAt, x.UpdatedAt);
}

public sealed record GetSubscriptionPlanByIdQuery(Guid Id) : IRequest<SubscriptionPlanDto?>;

public sealed class GetSubscriptionPlanByIdHandler : IRequestHandler<GetSubscriptionPlanByIdQuery, SubscriptionPlanDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;

    public GetSubscriptionPlanByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<SubscriptionPlanDto?> Handle(GetSubscriptionPlanByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.SubscriptionPlans
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId && x.DeletedAt == null, ct);
        return e is null ? null : GetSubscriptionPlansHandler.ToDto(e);
    }
}

// ── Admin subscription list (read) ───────────────────────────────────────────

public sealed record GetCustomerSubscriptionsAdminQuery(int Page, int PageSize, Guid? CustomerId, string? Status)
    : IRequest<PaginatedList<CustomerSubscriptionDto>>;

public sealed class GetCustomerSubscriptionsAdminHandler
    : IRequestHandler<GetCustomerSubscriptionsAdminQuery, PaginatedList<CustomerSubscriptionDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;

    public GetCustomerSubscriptionsAdminHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<CustomerSubscriptionDto>> Handle(GetCustomerSubscriptionsAdminQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.CustomerSubscriptions.Where(x => x.BrandId == brandId);

        if (q.CustomerId.HasValue) query = query.Where(x => x.CustomerId == q.CustomerId.Value);
        if (!string.IsNullOrEmpty(q.Status)) query = query.Where(x => x.Status == q.Status);

        return PaginatedList<CustomerSubscriptionDto>.CreateAsync(
            query.OrderByDescending(x => x.CreatedAt).Select(x => ToDto(x)),
            q.Page, q.PageSize, ct);
    }

    internal static CustomerSubscriptionDto ToDto(CustomerSubscription x) => new(
        x.Id, x.BrandId, x.CustomerId, x.PlanId, x.SubscriptionNumber,
        x.PriceSnapshot, x.BillingInterval, x.IntervalCount,
        x.QuotaType, x.QuotaValue, x.CurrencyCode, x.Status,
        x.AutoRenew, x.CurrentPeriodStart, x.CurrentPeriodEnd,
        x.NextBillingAt, x.CreditsRemaining, x.CancelAtPeriodEnd,
        x.CancelledAt, x.DunningAttempts, x.TotalCyclesBilled,
        x.CreatedAt, x.UpdatedAt);
}

// ── Commands ──────────────────────────────────────────────────────────────────

public sealed record CreateSubscriptionPlanCommand(CreateSubscriptionPlanRequest Request, Guid? ActorId)
    : IRequest<SubscriptionPlanDto>;

public sealed class CreateSubscriptionPlanHandler : IRequestHandler<CreateSubscriptionPlanCommand, SubscriptionPlanDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;

    public CreateSubscriptionPlanHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<SubscriptionPlanDto> Handle(CreateSubscriptionPlanCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        var entity = new SubscriptionPlan
        {
            Id                       = Guid.NewGuid(),
            BrandId                  = brandId,
            Code                     = req.Code,
            Name                     = req.Name,
            NameLocalized            = req.NameLocalized,
            Description              = req.Description,
            Tier                     = req.Tier,
            BillingInterval          = req.BillingInterval,
            IntervalCount            = req.IntervalCount,
            Price                    = req.Price,
            SetupFee                 = req.SetupFee,
            CurrencyCode             = req.CurrencyCode,
            TrialDays                = req.TrialDays,
            QuotaType                = req.QuotaType,
            QuotaValue               = req.QuotaValue,
            RolloverUnused           = req.RolloverUnused,
            MaxRollover              = req.MaxRollover,
            OverageDiscountPercent   = req.OverageDiscountPercent,
            ApplicableServices       = req.ApplicableServices ?? [],
            ExcludedServices         = req.ExcludedServices ?? [],
            PickupIncluded           = req.PickupIncluded,
            DeliveryIncluded         = req.DeliveryIncluded,
            ExpressIncluded          = req.ExpressIncluded,
            MaxActiveSubscribers     = req.MaxActiveSubscribers,
            CurrentSubscriberCount   = 0,
            Gateway                  = req.Gateway,
            GatewayPlanId            = req.GatewayPlanId,
            TermsAndConditions       = req.TermsAndConditions,
            IconUrl                  = req.IconUrl,
            ColorHex                 = req.ColorHex,
            DisplayOrder             = req.DisplayOrder,
            IsPublic                 = req.IsPublic,
            IsFeatured               = req.IsFeatured,
            Status                   = "draft",
            AvailableFrom            = req.AvailableFrom,
            AvailableTo              = req.AvailableTo,
            CreatedAt                = now,
            UpdatedAt                = now,
            CreatedBy                = cmd.ActorId,
            UpdatedBy                = cmd.ActorId,
            Version                  = 1
        };

        _db.SubscriptionPlans.Add(entity);
        await _db.SaveChangesAsync(ct);
        return GetSubscriptionPlansHandler.ToDto(entity);
    }
}

public sealed class CreateSubscriptionPlanValidator : AbstractValidator<CreateSubscriptionPlanCommand>
{
    public CreateSubscriptionPlanValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.NameLocalized).NotEmpty()
            .Must(BeValidJsonObject)
            .WithMessage("name_localized must be a valid JSON object string (e.g. {\"en\":\"Basic\",\"hi\":\"बेसिक\"})");
        RuleFor(x => x.Request.Tier).NotEmpty()
            .Must(v => v is "basic" or "standard" or "premium" or "custom")
            .WithMessage("tier must be one of: basic, standard, premium, custom");
        RuleFor(x => x.Request.BillingInterval).NotEmpty()
            .Must(v => v is "weekly" or "monthly" or "quarterly" or "half_yearly" or "yearly")
            .WithMessage("billing_interval must be one of: weekly, monthly, quarterly, half_yearly, yearly");
        RuleFor(x => x.Request.IntervalCount).GreaterThan((short)0);
        RuleFor(x => x.Request.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.SetupFee).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.QuotaType).NotEmpty()
            .Must(v => v is "credit" or "order_count" or "weight_kg" or "unlimited")
            .WithMessage("quota_type must be one of: credit, order_count, weight_kg, unlimited");
        RuleFor(x => x.Request.OverageDiscountPercent).InclusiveBetween(0, 100);
        RuleFor(x => x.Request.CurrencyCode).NotEmpty().Length(3);
    }

    internal static bool BeValidJsonObject(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var doc = JsonDocument.Parse(value);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed class UpdateSubscriptionPlanValidator : AbstractValidator<UpdateSubscriptionPlanCommand>
{
    public UpdateSubscriptionPlanValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.NameLocalized).NotEmpty()
            .Must(CreateSubscriptionPlanValidator.BeValidJsonObject)
            .WithMessage("name_localized must be a valid JSON object string (e.g. {\"en\":\"Basic\",\"hi\":\"बेसिक\"})");
        RuleFor(x => x.Request.Tier).NotEmpty()
            .Must(v => v is "basic" or "standard" or "premium" or "custom")
            .WithMessage("tier must be one of: basic, standard, premium, custom");
        RuleFor(x => x.Request.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.SetupFee).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.QuotaType).NotEmpty()
            .Must(v => v is "credit" or "order_count" or "weight_kg" or "unlimited")
            .WithMessage("quota_type must be one of: credit, order_count, weight_kg, unlimited");
        RuleFor(x => x.Request.OverageDiscountPercent).InclusiveBetween(0, 100);
        RuleFor(x => x.Request.Status).NotEmpty()
            .Must(v => v is "draft" or "active" or "paused" or "retired")
            .WithMessage("status must be one of: draft, active, paused, retired");
    }
}

public sealed record UpdateSubscriptionPlanCommand(Guid Id, UpdateSubscriptionPlanRequest Request, Guid? ActorId)
    : IRequest<SubscriptionPlanDto?>;

public sealed class UpdateSubscriptionPlanHandler : IRequestHandler<UpdateSubscriptionPlanCommand, SubscriptionPlanDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;

    public UpdateSubscriptionPlanHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<SubscriptionPlanDto?> Handle(UpdateSubscriptionPlanCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity  = await _db.SubscriptionPlans
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId && x.DeletedAt == null, ct);
        if (entity is null) return null;

        var req = cmd.Request;
        entity.Name                  = req.Name;
        entity.NameLocalized         = req.NameLocalized;
        entity.Description           = req.Description;
        entity.Tier                  = req.Tier;
        entity.Price                 = req.Price;
        entity.SetupFee              = req.SetupFee;
        entity.QuotaType             = req.QuotaType;
        entity.QuotaValue            = req.QuotaValue;
        entity.RolloverUnused        = req.RolloverUnused;
        entity.MaxRollover           = req.MaxRollover;
        entity.OverageDiscountPercent = req.OverageDiscountPercent;
        entity.ApplicableServices    = req.ApplicableServices ?? [];
        entity.ExcludedServices      = req.ExcludedServices ?? [];
        entity.PickupIncluded        = req.PickupIncluded;
        entity.DeliveryIncluded      = req.DeliveryIncluded;
        entity.ExpressIncluded       = req.ExpressIncluded;
        entity.MaxActiveSubscribers  = req.MaxActiveSubscribers;
        entity.Gateway               = req.Gateway;
        entity.GatewayPlanId         = req.GatewayPlanId;
        entity.TermsAndConditions    = req.TermsAndConditions;
        entity.IconUrl               = req.IconUrl;
        entity.ColorHex              = req.ColorHex;
        entity.DisplayOrder          = req.DisplayOrder;
        entity.IsPublic              = req.IsPublic;
        entity.IsFeatured            = req.IsFeatured;
        entity.Status                = req.Status;
        entity.AvailableFrom         = req.AvailableFrom;
        entity.AvailableTo           = req.AvailableTo;
        entity.UpdatedAt             = DateTimeOffset.UtcNow;
        entity.UpdatedBy             = cmd.ActorId;
        entity.Version++;

        await _db.SaveChangesAsync(ct);
        return GetSubscriptionPlansHandler.ToDto(entity);
    }
}

public sealed record DeleteSubscriptionPlanCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

public sealed class DeleteSubscriptionPlanHandler : IRequestHandler<DeleteSubscriptionPlanCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;

    public DeleteSubscriptionPlanHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> Handle(DeleteSubscriptionPlanCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity  = await _db.SubscriptionPlans
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId && x.DeletedAt == null, ct);
        if (entity is null) return false;

        // Guard: cannot delete a plan that has active subscribers
        if (entity.CurrentSubscriberCount > 0)
            throw new InvalidOperationException(
                $"Cannot delete a subscription plan with {entity.CurrentSubscriberCount} active subscriber(s). Retire it instead.");

        entity.DeletedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
