using FluentValidation;
using laundryghar.Engagement.Application.Cms.Dtos;
using MediatR;

namespace laundryghar.Engagement.Application.Cms.Commands;

// ── Create ─────────────────────────────────────────────────────────────────────

public sealed record CreateMobileAppConfigCommand(
    CreateMobileAppConfigRequest Request, Guid? ActorId) : IRequest<MobileAppConfigDto>;

public sealed class CreateMobileAppConfigHandler
    : IRequestHandler<CreateMobileAppConfigCommand, MobileAppConfigDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateMobileAppConfigHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<MobileAppConfigDto> Handle(CreateMobileAppConfigCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var entity = new MobileAppConfig
        {
            Id             = Guid.NewGuid(),
            BrandId        = brandId,
            AppType        = req.AppType,
            Platform       = req.Platform,
            ConfigKey      = req.ConfigKey,
            ConfigValue    = req.ConfigValue,
            Description    = req.Description,
            IsForceUpdate  = req.IsForceUpdate,
            MinAppVersion  = req.MinAppVersion,
            MaxAppVersion  = req.MaxAppVersion,
            TargetSegments = req.TargetSegments,
            RolloutPercent = req.RolloutPercent,
            IsActive       = req.IsActive,
            Status         = "active",
            CreatedAt      = now,
            UpdatedAt      = now,
            CreatedBy      = cmd.ActorId,
            UpdatedBy      = cmd.ActorId,
        };

        _db.MobileAppConfigs.Add(entity);
        await _db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    internal static MobileAppConfigDto ToDto(MobileAppConfig e) => new(
        e.Id, e.BrandId, e.AppType, e.Platform,
        e.ConfigKey, e.ConfigValue,
        e.Description, e.IsForceUpdate,
        e.MinAppVersion, e.MaxAppVersion,
        e.TargetSegments, e.RolloutPercent,
        e.IsActive, e.Status,
        e.CreatedAt, e.UpdatedAt);
}

// ── Update ─────────────────────────────────────────────────────────────────────

public sealed record UpdateMobileAppConfigCommand(
    Guid Id, UpdateMobileAppConfigRequest Request, Guid? ActorId) : IRequest<MobileAppConfigDto?>;

public sealed class UpdateMobileAppConfigHandler
    : IRequestHandler<UpdateMobileAppConfigCommand, MobileAppConfigDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateMobileAppConfigHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<MobileAppConfigDto?> Handle(UpdateMobileAppConfigCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.MobileAppConfigs
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (entity is null) return null;

        var req = cmd.Request;
        entity.AppType        = req.AppType;
        entity.Platform       = req.Platform;
        entity.ConfigKey      = req.ConfigKey;
        entity.ConfigValue    = req.ConfigValue;
        entity.Description    = req.Description;
        entity.IsForceUpdate  = req.IsForceUpdate;
        entity.MinAppVersion  = req.MinAppVersion;
        entity.MaxAppVersion  = req.MaxAppVersion;
        entity.TargetSegments = req.TargetSegments;
        entity.RolloutPercent = req.RolloutPercent;
        entity.IsActive       = req.IsActive;
        entity.Status         = req.Status;
        entity.UpdatedAt      = DateTimeOffset.UtcNow;
        entity.UpdatedBy      = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return CreateMobileAppConfigHandler.ToDto(entity);
    }
}

// ── Delete ─────────────────────────────────────────────────────────────────────

public sealed record DeleteMobileAppConfigCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

public sealed class DeleteMobileAppConfigHandler
    : IRequestHandler<DeleteMobileAppConfigCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteMobileAppConfigHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<bool> Handle(DeleteMobileAppConfigCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.MobileAppConfigs
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (entity is null) return false;

        entity.Status    = "archived";
        entity.IsActive  = false;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

// ── Outbox Retry Command ───────────────────────────────────────────────────────

public sealed record RetryNotificationOutboxCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

public sealed class RetryNotificationOutboxHandler
    : IRequestHandler<RetryNotificationOutboxCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public RetryNotificationOutboxHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<bool> Handle(RetryNotificationOutboxCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.NotificationOutboxes
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);

        if (entity is null) return false;
        if (entity.Status != "failed")
            throw new BusinessRuleException($"Cannot retry outbox entry with status '{entity.Status}'. Only 'failed' entries can be retried.");

        entity.Status        = "pending";
        entity.NextAttemptAt = DateTimeOffset.UtcNow;
        entity.LastError     = null;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

// ── Validators ─────────────────────────────────────────────────────────────────

public sealed class CreateMobileAppConfigValidator
    : AbstractValidator<CreateMobileAppConfigCommand>
{
    private static readonly string[] ValidPlatforms = ["android", "ios", "web"];

    public CreateMobileAppConfigValidator()
    {
        RuleFor(x => x.Request.Platform).NotEmpty()
            .Must(p => ValidPlatforms.Contains(p))
            .WithMessage("platform must be one of: android, ios, web");
        RuleFor(x => x.Request.ConfigKey).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.ConfigValue).NotEmpty();
        RuleFor(x => x.Request.AppType).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Request.RolloutPercent)
            .InclusiveBetween((short)0, (short)100)
            .When(x => x.Request.RolloutPercent.HasValue)
            .WithMessage("rollout_percent must be between 0 and 100");
    }
}
