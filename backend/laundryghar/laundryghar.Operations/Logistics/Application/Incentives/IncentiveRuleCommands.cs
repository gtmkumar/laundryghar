using FluentValidation;
using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.SharedDataModel.Enums;
using MediatR;

namespace laundryghar.Logistics.Application.Incentives;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record IncentiveRuleDto(
    Guid Id,
    string Name,
    string RuleType,
    int Threshold,
    decimal RewardAmount,
    string Window,
    bool IsActive,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidUntil);

public sealed record UpsertIncentiveRuleRequest(
    string Name,
    string RuleType,
    int Threshold,
    decimal RewardAmount,
    bool IsActive = true,
    DateTimeOffset? ValidUntil = null);

// ── List ─────────────────────────────────────────────────────────────────────

public sealed record GetIncentiveRulesQuery(Guid BrandId, bool? ActiveOnly)
    : IRequest<IReadOnlyList<IncentiveRuleDto>>;

public sealed class GetIncentiveRulesHandler : IRequestHandler<GetIncentiveRulesQuery, IReadOnlyList<IncentiveRuleDto>>
{
    private readonly LaundryGharDbContext _db;
    public GetIncentiveRulesHandler(LaundryGharDbContext db) => _db = db;

    public async Task<IReadOnlyList<IncentiveRuleDto>> Handle(GetIncentiveRulesQuery q, CancellationToken ct)
        => await _db.IncentiveRules.AsNoTracking()
            .Where(r => r.BrandId == q.BrandId && (q.ActiveOnly != true || r.IsActive))
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new IncentiveRuleDto(r.Id, r.Name, r.RuleType, r.Threshold, r.RewardAmount,
                r.Window, r.IsActive, r.ValidFrom, r.ValidUntil))
            .ToListAsync(ct);
}

// ── Create / Update / Delete ─────────────────────────────────────────────────

public sealed record CreateIncentiveRuleCommand(Guid BrandId, UpsertIncentiveRuleRequest Request, Guid? ActorId)
    : IRequest<IncentiveRuleDto>;

public sealed class CreateIncentiveRuleHandler : IRequestHandler<CreateIncentiveRuleCommand, IncentiveRuleDto>
{
    private readonly LaundryGharDbContext _db;
    public CreateIncentiveRuleHandler(LaundryGharDbContext db) => _db = db;

    public async Task<IncentiveRuleDto> Handle(CreateIncentiveRuleCommand cmd, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var r = new IncentiveRule
        {
            Id = Guid.NewGuid(),
            BrandId = cmd.BrandId,
            Name = cmd.Request.Name.Trim(),
            RuleType = cmd.Request.RuleType,
            Threshold = cmd.Request.Threshold,
            RewardAmount = cmd.Request.RewardAmount,
            Window = "daily",
            IsActive = cmd.Request.IsActive,
            ValidFrom = now,
            ValidUntil = cmd.Request.ValidUntil,
            Metadata = "{}",
            CreatedAt = now, UpdatedAt = now, CreatedBy = cmd.ActorId, UpdatedBy = cmd.ActorId,
        };
        _db.IncentiveRules.Add(r);
        await _db.SaveChangesAsync(ct);
        return ToDto(r);
    }

    internal static IncentiveRuleDto ToDto(IncentiveRule r) =>
        new(r.Id, r.Name, r.RuleType, r.Threshold, r.RewardAmount, r.Window, r.IsActive, r.ValidFrom, r.ValidUntil);
}

public sealed record UpdateIncentiveRuleCommand(Guid Id, Guid BrandId, UpsertIncentiveRuleRequest Request, Guid? ActorId)
    : IRequest<IncentiveRuleDto?>;

public sealed class UpdateIncentiveRuleHandler : IRequestHandler<UpdateIncentiveRuleCommand, IncentiveRuleDto?>
{
    private readonly LaundryGharDbContext _db;
    public UpdateIncentiveRuleHandler(LaundryGharDbContext db) => _db = db;

    public async Task<IncentiveRuleDto?> Handle(UpdateIncentiveRuleCommand cmd, CancellationToken ct)
    {
        var r = await _db.IncentiveRules.FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == cmd.BrandId, ct);
        if (r is null) return null;

        r.Name = cmd.Request.Name.Trim();
        r.RuleType = cmd.Request.RuleType;
        r.Threshold = cmd.Request.Threshold;
        r.RewardAmount = cmd.Request.RewardAmount;
        r.IsActive = cmd.Request.IsActive;
        r.ValidUntil = cmd.Request.ValidUntil;
        r.UpdatedAt = DateTimeOffset.UtcNow;
        r.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return CreateIncentiveRuleHandler.ToDto(r);
    }
}

public sealed record DeleteIncentiveRuleCommand(Guid Id, Guid BrandId) : IRequest<bool>;

public sealed class DeleteIncentiveRuleHandler : IRequestHandler<DeleteIncentiveRuleCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    public DeleteIncentiveRuleHandler(LaundryGharDbContext db) => _db = db;

    public async Task<bool> Handle(DeleteIncentiveRuleCommand cmd, CancellationToken ct)
    {
        var r = await _db.IncentiveRules.FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == cmd.BrandId, ct);
        if (r is null) return false;
        _db.IncentiveRules.Remove(r);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class UpsertIncentiveRuleValidator : AbstractValidator<UpsertIncentiveRuleRequest>
{
    public UpsertIncentiveRuleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.RuleType).Must(IncentiveRuleType.IsValid)
            .WithMessage($"RuleType must be one of: {string.Join(", ", IncentiveRuleType.All)}.");
        RuleFor(x => x.RewardAmount).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.Threshold).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Threshold).GreaterThan(0)
            .When(x => x.RuleType == IncentiveRuleType.TripsTarget)
            .WithMessage("trips_target requires a threshold greater than 0.");
    }
}
