using System.Text.Json;
using FluentValidation;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty.Subscriptions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Exceptions;
using MediatR;

namespace laundryghar.Finance.Application.Subscriptions;

// ── Queries ───────────────────────────────────────────────────────────────────

public sealed record GetPlatformPlansQuery(int Page, int PageSize, string? Status)
    : IRequest<PaginatedList<PlatformPlanDto>>;

public sealed class GetPlatformPlansHandler : IRequestHandler<GetPlatformPlansQuery, PaginatedList<PlatformPlanDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;

    public GetPlatformPlansHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<PlatformPlanDto>> Handle(GetPlatformPlansQuery q, CancellationToken ct)
    {
        // Platform admins can see all plans (global + brand-specific).
        // Brand admins can only see global plans and their brand's plans.
        var query = _db.PlatformPlans.AsQueryable();
        if (!_user.IsPlatformAdmin)
        {
            var brandId = _user.RequireBrandId();
            query = query.Where(p => p.BrandId == null || p.BrandId == brandId);
        }

        if (!string.IsNullOrEmpty(q.Status)) query = query.Where(p => p.Status == q.Status);

        return PaginatedList<PlatformPlanDto>.CreateAsync(
            query.OrderBy(p => p.DisplayOrder).ThenBy(p => p.Price).Select(p => ToDto(p)),
            q.Page, q.PageSize, ct);
    }

    internal static PlatformPlanDto ToDto(PlatformPlan p) => new(
        p.Id, p.BrandId, p.Code, p.Name, p.Description, p.Tier,
        p.BillingInterval, p.IntervalCount, p.Price, p.SetupFee,
        p.AnnualDiscountPercent, p.CurrencyCode, p.TrialDays,
        p.MaxStores, p.MaxWarehouses, p.MaxUsers, p.MaxOrdersPerMonth, p.MaxRiders,
        p.OveragePerOrder, p.OveragePerStore, p.OveragePerUser,
        p.Features, p.SupportLevel, p.IsPublic, p.IsFeatured, p.DisplayOrder,
        p.Status, p.CreatedAt, p.UpdatedAt);
}

public sealed record GetPlatformPlanByIdQuery(Guid Id) : IRequest<PlatformPlanDto?>;

public sealed class GetPlatformPlanByIdHandler : IRequestHandler<GetPlatformPlanByIdQuery, PlatformPlanDto?>
{
    private readonly LaundryGharDbContext _db;

    public GetPlatformPlanByIdHandler(LaundryGharDbContext db) => _db = db;

    public async Task<PlatformPlanDto?> Handle(GetPlatformPlanByIdQuery q, CancellationToken ct)
    {
        var e = await _db.PlatformPlans.FirstOrDefaultAsync(p => p.Id == q.Id && p.DeletedAt == null, ct);
        return e is null ? null : GetPlatformPlansHandler.ToDto(e);
    }
}

// ── Commands ──────────────────────────────────────────────────────────────────

public sealed record CreatePlatformPlanCommand(CreatePlatformPlanRequest Request, Guid? ActorId)
    : IRequest<PlatformPlanDto>;

public sealed class CreatePlatformPlanHandler : IRequestHandler<CreatePlatformPlanCommand, PlatformPlanDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;

    public CreatePlatformPlanHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PlatformPlanDto> Handle(CreatePlatformPlanCommand cmd, CancellationToken ct)
    {
        // Only platform admins may create platform plans
        if (!_user.IsPlatformAdmin)
            throw new UnauthorizedAccessException("Only platform administrators may manage SaaS plans.");

        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var entity = new PlatformPlan
        {
            Id                     = Guid.NewGuid(),
            BrandId                = req.BrandId,
            Code                   = req.Code,
            Name                   = req.Name,
            Description            = req.Description,
            Tier                   = req.Tier,
            BillingInterval        = req.BillingInterval,
            IntervalCount          = req.IntervalCount,
            Price                  = req.Price,
            SetupFee               = req.SetupFee,
            AnnualDiscountPercent  = req.AnnualDiscountPercent,
            CurrencyCode           = req.CurrencyCode,
            TrialDays              = req.TrialDays,
            MaxStores              = req.MaxStores,
            MaxWarehouses          = req.MaxWarehouses,
            MaxUsers               = req.MaxUsers,
            MaxOrdersPerMonth      = req.MaxOrdersPerMonth,
            MaxRiders              = req.MaxRiders,
            OveragePerOrder        = req.OveragePerOrder,
            OveragePerStore        = req.OveragePerStore,
            OveragePerUser         = req.OveragePerUser,
            Features               = req.Features,
            SupportLevel           = req.SupportLevel,
            IsPublic               = req.IsPublic,
            IsFeatured             = req.IsFeatured,
            DisplayOrder           = req.DisplayOrder,
            Status                 = "draft",
            CreatedAt              = now,
            UpdatedAt              = now,
            CreatedBy              = cmd.ActorId,
            UpdatedBy              = cmd.ActorId,
            Version                = 1
        };

        _db.PlatformPlans.Add(entity);
        await _db.SaveChangesAsync(ct);
        return GetPlatformPlansHandler.ToDto(entity);
    }
}

public sealed class CreatePlatformPlanValidator : AbstractValidator<CreatePlatformPlanCommand>
{
    public CreatePlatformPlanValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.Tier).NotEmpty()
            .Must(v => v is "starter" or "growth" or "pro" or "enterprise" or "custom")
            .WithMessage("tier must be one of: starter, growth, pro, enterprise, custom");
        RuleFor(x => x.Request.BillingInterval).NotEmpty()
            .Must(v => v is "monthly" or "quarterly" or "yearly")
            .WithMessage("billing_interval must be one of: monthly, quarterly, yearly");
        RuleFor(x => x.Request.IntervalCount).GreaterThan((short)0);
        RuleFor(x => x.Request.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.SetupFee).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.Request.SupportLevel).NotEmpty()
            .Must(v => v is "community" or "email" or "priority" or "dedicated")
            .WithMessage("support_level must be one of: community, email, priority, dedicated");
        RuleFor(x => x.Request.Features).NotEmpty()
            .Must(BeValidJsonObject)
            .WithMessage("features must be a valid JSON object string (e.g. {\"sms_alerts\":true,\"max_api_calls\":1000})");
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

public sealed class UpdatePlatformPlanValidator : AbstractValidator<UpdatePlatformPlanCommand>
{
    public UpdatePlatformPlanValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.Tier).NotEmpty()
            .Must(v => v is "starter" or "growth" or "pro" or "enterprise" or "custom")
            .WithMessage("tier must be one of: starter, growth, pro, enterprise, custom");
        RuleFor(x => x.Request.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.SetupFee).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.SupportLevel).NotEmpty()
            .Must(v => v is "community" or "email" or "priority" or "dedicated")
            .WithMessage("support_level must be one of: community, email, priority, dedicated");
        RuleFor(x => x.Request.Features).NotEmpty()
            .Must(CreatePlatformPlanValidator.BeValidJsonObject)
            .WithMessage("features must be a valid JSON object string (e.g. {\"sms_alerts\":true,\"max_api_calls\":1000})");
        RuleFor(x => x.Request.Status).NotEmpty()
            .Must(v => v is "draft" or "active" or "retired")
            .WithMessage("status must be one of: draft, active, retired");
    }
}

public sealed record UpdatePlatformPlanCommand(Guid Id, UpdatePlatformPlanRequest Request, Guid? ActorId)
    : IRequest<PlatformPlanDto?>;

public sealed class UpdatePlatformPlanHandler : IRequestHandler<UpdatePlatformPlanCommand, PlatformPlanDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;

    public UpdatePlatformPlanHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PlatformPlanDto?> Handle(UpdatePlatformPlanCommand cmd, CancellationToken ct)
    {
        if (!_user.IsPlatformAdmin)
            throw new UnauthorizedAccessException("Only platform administrators may manage SaaS plans.");

        var entity = await _db.PlatformPlans.FirstOrDefaultAsync(p => p.Id == cmd.Id && p.DeletedAt == null, ct);
        if (entity is null) return null;

        var req = cmd.Request;
        entity.Name                 = req.Name;
        entity.Description          = req.Description;
        entity.Tier                 = req.Tier;
        entity.Price                = req.Price;
        entity.SetupFee             = req.SetupFee;
        entity.AnnualDiscountPercent = req.AnnualDiscountPercent;
        entity.MaxStores            = req.MaxStores;
        entity.MaxWarehouses        = req.MaxWarehouses;
        entity.MaxUsers             = req.MaxUsers;
        entity.MaxOrdersPerMonth    = req.MaxOrdersPerMonth;
        entity.MaxRiders            = req.MaxRiders;
        entity.OveragePerOrder      = req.OveragePerOrder;
        entity.OveragePerStore      = req.OveragePerStore;
        entity.OveragePerUser       = req.OveragePerUser;
        entity.Features             = req.Features;
        entity.SupportLevel         = req.SupportLevel;
        entity.IsPublic             = req.IsPublic;
        entity.IsFeatured           = req.IsFeatured;
        entity.DisplayOrder         = req.DisplayOrder;
        entity.Status               = req.Status;
        entity.UpdatedAt            = DateTimeOffset.UtcNow;
        entity.UpdatedBy            = cmd.ActorId;
        entity.Version++;

        await _db.SaveChangesAsync(ct);
        return GetPlatformPlansHandler.ToDto(entity);
    }
}

// ── PATCH: status-only update ──────────────────────────────────────────────────

public sealed record PatchPlatformPlanStatusCommand(Guid Id, string Status, Guid? ActorId)
    : IRequest<PlatformPlanDto?>;

public sealed class PatchPlatformPlanStatusHandler
    : IRequestHandler<PatchPlatformPlanStatusCommand, PlatformPlanDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;

    public PatchPlatformPlanStatusHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<PlatformPlanDto?> Handle(PatchPlatformPlanStatusCommand cmd, CancellationToken ct)
    {
        if (!_user.IsPlatformAdmin)
            throw new UnauthorizedAccessException("Only platform administrators may manage SaaS plans.");

        var entity = await _db.PlatformPlans
            .FirstOrDefaultAsync(p => p.Id == cmd.Id && p.DeletedAt == null, ct);
        if (entity is null) return null;

        entity.Status    = cmd.Status;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = cmd.ActorId;
        entity.Version++;

        await _db.SaveChangesAsync(ct);
        return GetPlatformPlansHandler.ToDto(entity);
    }
}

public sealed class PatchPlatformPlanStatusValidator : AbstractValidator<PatchPlatformPlanStatusCommand>
{
    public PatchPlatformPlanStatusValidator()
    {
        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => s is "draft" or "active" or "retired")
            .WithMessage("status must be one of: draft, active, retired");
    }
}

public sealed record DeletePlatformPlanCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

public sealed class DeletePlatformPlanHandler : IRequestHandler<DeletePlatformPlanCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;

    public DeletePlatformPlanHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> Handle(DeletePlatformPlanCommand cmd, CancellationToken ct)
    {
        if (!_user.IsPlatformAdmin)
            throw new UnauthorizedAccessException("Only platform administrators may manage SaaS plans.");

        var entity = await _db.PlatformPlans.FirstOrDefaultAsync(p => p.Id == cmd.Id && p.DeletedAt == null, ct);
        if (entity is null) return false;

        var hasActiveSubscriptions = await _db.FranchiseSubscriptions.AnyAsync(
            fs => fs.PlatformPlanId == cmd.Id
               && fs.Status != "cancelled" && fs.Status != "expired", ct);
        if (hasActiveSubscriptions)
            throw new InvalidOperationException("Cannot delete a plan that has active franchise subscriptions. Retire it instead.");

        entity.DeletedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
