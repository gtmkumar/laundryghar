using commerce.Application.Common.Interfaces;
using FluentValidation;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Commerce.Admin.LoyaltyPrograms;

// ── Queries ───────────────────────────────────────────────────────────────────

public sealed record GetLoyaltyProgramsQuery(int Page, int PageSize) : IQuery<PaginatedList<LoyaltyProgramDto>>;

public sealed class GetLoyaltyProgramsHandler : IQueryHandler<GetLoyaltyProgramsQuery, PaginatedList<LoyaltyProgramDto>>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public GetLoyaltyProgramsHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<LoyaltyProgramDto>> HandleAsync(GetLoyaltyProgramsQuery q, CancellationToken ct)
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

public sealed record GetLoyaltyProgramByIdQuery(Guid Id) : IQuery<LoyaltyProgramDto?>;

public sealed class GetLoyaltyProgramByIdHandler : IQueryHandler<GetLoyaltyProgramByIdQuery, LoyaltyProgramDto?>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public GetLoyaltyProgramByIdHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<LoyaltyProgramDto?> HandleAsync(GetLoyaltyProgramByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.LoyaltyPrograms.FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : GetLoyaltyProgramsHandler.ToDto(e);
    }
}

// ── Commands ──────────────────────────────────────────────────────────────────

public sealed record CreateLoyaltyProgramCommand(CreateLoyaltyProgramRequest Request, Guid? ActorId) : ICommand<LoyaltyProgramDto>;

public sealed class CreateLoyaltyProgramHandler : ICommandHandler<CreateLoyaltyProgramCommand, LoyaltyProgramDto>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public CreateLoyaltyProgramHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<LoyaltyProgramDto> HandleAsync(CreateLoyaltyProgramCommand cmd, CancellationToken ct)
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

public sealed class CreateLoyaltyProgramValidator : AbstractValidator<CreateLoyaltyProgramRequest>
{
    public CreateLoyaltyProgramValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EarnBasis).NotEmpty();
        RuleFor(x => x.EarnRate).GreaterThanOrEqualTo(0);
        RuleFor(x => x.BurnRate).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TierConfig).NotEmpty();
    }
}

public sealed record UpdateLoyaltyProgramCommand(Guid Id, UpdateLoyaltyProgramRequest Request, Guid? ActorId) : ICommand<LoyaltyProgramDto?>;

public sealed class UpdateLoyaltyProgramHandler : ICommandHandler<UpdateLoyaltyProgramCommand, LoyaltyProgramDto?>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateLoyaltyProgramHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<LoyaltyProgramDto?> HandleAsync(UpdateLoyaltyProgramCommand cmd, CancellationToken ct)
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

public sealed record DeleteLoyaltyProgramCommand(Guid Id) : ICommand<bool>;

public sealed class DeleteLoyaltyProgramHandler : ICommandHandler<DeleteLoyaltyProgramCommand, bool>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteLoyaltyProgramHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> HandleAsync(DeleteLoyaltyProgramCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.LoyaltyPrograms.FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (entity is null) return false;

        _db.LoyaltyPrograms.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
