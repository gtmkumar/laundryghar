using System.Text.Json;
using commerce.Application.Common.Interfaces;
using commerce.Application.Finance.Subscriptions.Dtos;
using commerce.Application.Finance.Subscriptions.Queries;
using FluentValidation;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty.Subscriptions;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Finance.Subscriptions.Commands;

// ── Create Platform Plan ──────────────────────────────────────────────────────

public sealed record CreatePlatformPlanCommand(CreatePlatformPlanRequest Request, Guid? ActorId)
    : ICommand<PlatformPlanDto>;

public sealed class CreatePlatformPlanHandler : ICommandHandler<CreatePlatformPlanCommand, PlatformPlanDto>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;

    public CreatePlatformPlanHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PlatformPlanDto> HandleAsync(CreatePlatformPlanCommand cmd, CancellationToken ct)
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

public sealed class CreatePlatformPlanValidator : AbstractValidator<CreatePlatformPlanRequest>
{
    public CreatePlatformPlanValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Tier).NotEmpty()
            .Must(v => v is "starter" or "growth" or "pro" or "enterprise" or "custom")
            .WithMessage("tier must be one of: starter, growth, pro, enterprise, custom");
        RuleFor(x => x.BillingInterval).NotEmpty()
            .Must(v => v is "monthly" or "quarterly" or "yearly")
            .WithMessage("billing_interval must be one of: monthly, quarterly, yearly");
        RuleFor(x => x.IntervalCount).GreaterThan((short)0);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.SetupFee).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.SupportLevel).NotEmpty()
            .Must(v => v is "community" or "email" or "priority" or "dedicated")
            .WithMessage("support_level must be one of: community, email, priority, dedicated");
        RuleFor(x => x.Features).NotEmpty()
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

// ── Update Platform Plan ──────────────────────────────────────────────────────

public sealed record UpdatePlatformPlanCommand(Guid Id, UpdatePlatformPlanRequest Request, Guid? ActorId)
    : ICommand<PlatformPlanDto?>;

public sealed class UpdatePlatformPlanHandler : ICommandHandler<UpdatePlatformPlanCommand, PlatformPlanDto?>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;

    public UpdatePlatformPlanHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PlatformPlanDto?> HandleAsync(UpdatePlatformPlanCommand cmd, CancellationToken ct)
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

public sealed class UpdatePlatformPlanValidator : AbstractValidator<UpdatePlatformPlanRequest>
{
    public UpdatePlatformPlanValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Tier).NotEmpty()
            .Must(v => v is "starter" or "growth" or "pro" or "enterprise" or "custom")
            .WithMessage("tier must be one of: starter, growth, pro, enterprise, custom");
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.SetupFee).GreaterThanOrEqualTo(0);
        RuleFor(x => x.SupportLevel).NotEmpty()
            .Must(v => v is "community" or "email" or "priority" or "dedicated")
            .WithMessage("support_level must be one of: community, email, priority, dedicated");
        RuleFor(x => x.Features).NotEmpty()
            .Must(CreatePlatformPlanValidator.BeValidJsonObject)
            .WithMessage("features must be a valid JSON object string (e.g. {\"sms_alerts\":true,\"max_api_calls\":1000})");
        RuleFor(x => x.Status).NotEmpty()
            .Must(v => v is "draft" or "active" or "retired")
            .WithMessage("status must be one of: draft, active, retired");
    }
}

// ── PATCH: status-only update ──────────────────────────────────────────────────

public sealed record PatchPlatformPlanStatusCommand(Guid Id, string Status, Guid? ActorId)
    : ICommand<PlatformPlanDto?>;

public sealed class PatchPlatformPlanStatusHandler
    : ICommandHandler<PatchPlatformPlanStatusCommand, PlatformPlanDto?>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;

    public PatchPlatformPlanStatusHandler(ICommerceDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<PlatformPlanDto?> HandleAsync(PatchPlatformPlanStatusCommand cmd, CancellationToken ct)
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

public sealed class PatchPlatformPlanStatusValidator : AbstractValidator<PatchPlatformPlanStatusRequest>
{
    public PatchPlatformPlanStatusValidator()
    {
        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => s is "draft" or "active" or "retired")
            .WithMessage("status must be one of: draft, active, retired");
    }
}

// ── Delete Platform Plan (soft) ───────────────────────────────────────────────

public sealed record DeletePlatformPlanCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public sealed class DeletePlatformPlanHandler : ICommandHandler<DeletePlatformPlanCommand, bool>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;

    public DeletePlatformPlanHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> HandleAsync(DeletePlatformPlanCommand cmd, CancellationToken ct)
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

        // Soft-delete must also move status off active so status-keyed reports don't
        // miscount it. platform_plans CHECK is ('draft','active','retired'); 'retired'
        // is terminal.
        entity.Status    = "retired";
        entity.DeletedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
