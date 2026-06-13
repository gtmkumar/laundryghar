using FluentValidation;
using laundryghar.Commerce.Application;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Commerce.Application.Admin.LoyaltyPrograms;

// ── Queries ───────────────────────────────────────────────────────────────────

public sealed record GetLoyaltyProgramsQuery(int Page, int PageSize) : IRequest<PaginatedList<LoyaltyProgramDto>>;

public sealed class GetLoyaltyProgramsHandler : IRequestHandler<GetLoyaltyProgramsQuery, PaginatedList<LoyaltyProgramDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetLoyaltyProgramsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<LoyaltyProgramDto>> Handle(GetLoyaltyProgramsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query = _db.LoyaltyPrograms
            .Where(x => x.BrandId == brandId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => ToDto(x));
        return PaginatedList<LoyaltyProgramDto>.CreateAsync(query, q.Page, q.PageSize, ct);
    }

    internal static LoyaltyProgramDto ToDto(LoyaltyProgram x) => new(
        x.Id, x.BrandId, x.Code, x.Name, x.Description, x.IsActive,
        x.EarnRate, x.EarnBasis, x.BurnRate, x.MinBurnPoints, x.MaxBurnPerOrderPct, x.MinOrderForEarn,
        x.ExcludedServices, x.PointExpiryMonths, x.WelcomeBonus,
        x.ReferralBonusReferrer, x.ReferralBonusReferee, x.BirthdayBonus,
        x.TierConfig, x.Terms, x.LaunchedAt, x.Status, x.CreatedAt, x.UpdatedAt);
}

public sealed record GetLoyaltyProgramByIdQuery(Guid Id) : IRequest<LoyaltyProgramDto?>;

public sealed class GetLoyaltyProgramByIdHandler : IRequestHandler<GetLoyaltyProgramByIdQuery, LoyaltyProgramDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetLoyaltyProgramByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<LoyaltyProgramDto?> Handle(GetLoyaltyProgramByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.LoyaltyPrograms.FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : GetLoyaltyProgramsHandler.ToDto(e);
    }
}

// ── Commands ──────────────────────────────────────────────────────────────────

public sealed record CreateLoyaltyProgramCommand(CreateLoyaltyProgramRequest Request, Guid? ActorId) : IRequest<LoyaltyProgramDto>;

public sealed class CreateLoyaltyProgramHandler : IRequestHandler<CreateLoyaltyProgramCommand, LoyaltyProgramDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateLoyaltyProgramHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<LoyaltyProgramDto> Handle(CreateLoyaltyProgramCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        // Enforce one program per brand
        var existing = await _db.LoyaltyPrograms.AnyAsync(x => x.BrandId == brandId, ct);
        if (existing)
            throw new BusinessRuleException("A loyalty program already exists for this brand. Update the existing program.");

        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var entity = new LoyaltyProgram
        {
            Id                    = Guid.NewGuid(),
            BrandId               = brandId,
            Code                  = req.Code,
            Name                  = req.Name,
            Description           = req.Description,
            IsActive              = req.IsActive,
            EarnRate              = req.EarnRate,
            EarnBasis             = req.EarnBasis,
            BurnRate              = req.BurnRate,
            MinBurnPoints         = req.MinBurnPoints,
            MaxBurnPerOrderPct    = req.MaxBurnPerOrderPct,
            MinOrderForEarn       = req.MinOrderForEarn,
            ExcludedServices      = req.ExcludedServices ?? [],
            PointExpiryMonths     = req.PointExpiryMonths,
            WelcomeBonus          = req.WelcomeBonus,
            ReferralBonusReferrer = req.ReferralBonusReferrer,
            ReferralBonusReferee  = req.ReferralBonusReferee,
            BirthdayBonus         = req.BirthdayBonus,
            TierConfig            = req.TierConfig,
            Terms                 = req.Terms,
            Status                = "active",
            CreatedAt             = now,
            UpdatedAt             = now,
            CreatedBy             = cmd.ActorId,
            UpdatedBy             = cmd.ActorId
        };

        _db.LoyaltyPrograms.Add(entity);
        await _db.SaveChangesAsync(ct);
        return GetLoyaltyProgramsHandler.ToDto(entity);
    }
}

public sealed class CreateLoyaltyProgramValidator : AbstractValidator<CreateLoyaltyProgramCommand>
{
    public CreateLoyaltyProgramValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.EarnBasis).NotEmpty();
        RuleFor(x => x.Request.EarnRate).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.BurnRate).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.TierConfig).NotEmpty();
    }
}

public sealed record UpdateLoyaltyProgramCommand(Guid Id, UpdateLoyaltyProgramRequest Request, Guid? ActorId) : IRequest<LoyaltyProgramDto?>;

public sealed class UpdateLoyaltyProgramHandler : IRequestHandler<UpdateLoyaltyProgramCommand, LoyaltyProgramDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateLoyaltyProgramHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<LoyaltyProgramDto?> Handle(UpdateLoyaltyProgramCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.LoyaltyPrograms.FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (entity is null) return null;

        var req = cmd.Request;
        entity.Name                  = req.Name;
        entity.Description           = req.Description;
        entity.IsActive              = req.IsActive;
        entity.EarnRate              = req.EarnRate;
        entity.EarnBasis             = req.EarnBasis;
        entity.BurnRate              = req.BurnRate;
        entity.MinBurnPoints         = req.MinBurnPoints;
        entity.MaxBurnPerOrderPct    = req.MaxBurnPerOrderPct;
        entity.MinOrderForEarn       = req.MinOrderForEarn;
        entity.ExcludedServices      = req.ExcludedServices ?? [];
        entity.PointExpiryMonths     = req.PointExpiryMonths;
        entity.WelcomeBonus          = req.WelcomeBonus;
        entity.ReferralBonusReferrer = req.ReferralBonusReferrer;
        entity.ReferralBonusReferee  = req.ReferralBonusReferee;
        entity.BirthdayBonus         = req.BirthdayBonus;
        entity.TierConfig            = req.TierConfig;
        entity.Terms                 = req.Terms;
        entity.Status                = req.Status;
        entity.UpdatedAt             = DateTimeOffset.UtcNow;
        entity.UpdatedBy             = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return GetLoyaltyProgramsHandler.ToDto(entity);
    }
}

public sealed record DeleteLoyaltyProgramCommand(Guid Id) : IRequest<bool>;

public sealed class DeleteLoyaltyProgramHandler : IRequestHandler<DeleteLoyaltyProgramCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteLoyaltyProgramHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> Handle(DeleteLoyaltyProgramCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.LoyaltyPrograms.FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (entity is null) return false;

        _db.LoyaltyPrograms.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
