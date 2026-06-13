using FluentValidation;
using laundryghar.Catalog.Application.Pricing.Dtos;
using MediatR;

namespace laundryghar.Catalog.Application.Pricing.Commands;

// ── Create PriceList ──────────────────────────────────────────────────────────

public sealed record CreatePriceListCommand(CreatePriceListRequest Request, Guid? ActorId) : IRequest<PriceListDto>;

public sealed class CreatePriceListHandler : IRequestHandler<CreatePriceListCommand, PriceListDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreatePriceListHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<PriceListDto> Handle(CreatePriceListCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        // Business version: start at 1
        var e = new PriceList
        {
            Id                = Guid.NewGuid(),
            BrandId           = brandId,
            FranchiseId       = req.FranchiseId,
            StoreId           = req.StoreId,
            Code              = req.Code,
            Name              = req.Name,
            Description       = req.Description,
            CurrencyCode      = req.CurrencyCode,
            ScopeType         = req.ScopeType,
            VersionNumber     = 1,
            ParentPriceListId = req.ParentPriceListId,
            EffectiveFrom     = req.EffectiveFrom,
            EffectiveTo       = req.EffectiveTo,
            IsDefault         = req.IsDefault,
            IsPublished       = false,
            Status            = "draft",
            Notes             = req.Notes,
            CreatedAt         = now,
            UpdatedAt         = now,
            CreatedBy         = cmd.ActorId,
            UpdatedBy         = cmd.ActorId,
            Version           = 1
        };

        _db.PriceLists.Add(e);
        await _db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    internal static PriceListDto ToDto(PriceList e) => new(
        e.Id, e.BrandId, e.FranchiseId, e.StoreId, e.Code, e.Name, e.Description,
        e.CurrencyCode, e.ScopeType, e.VersionNumber, e.ParentPriceListId,
        e.EffectiveFrom, e.EffectiveTo, e.IsDefault, e.IsPublished, e.PublishedAt,
        e.Status, e.Notes, e.CreatedAt, e.UpdatedAt);
}

// ── Update PriceList ──────────────────────────────────────────────────────────

public sealed record UpdatePriceListCommand(Guid Id, UpdatePriceListRequest Request, Guid? ActorId) : IRequest<PriceListDto?>;

public sealed class UpdatePriceListHandler : IRequestHandler<UpdatePriceListCommand, PriceListDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdatePriceListHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PriceListDto?> Handle(UpdatePriceListCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.PriceLists
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return null;

        if (e.IsPublished)
            throw new BusinessRuleException("Published price lists cannot be modified. Create a new version.");

        var req = cmd.Request;
        e.Name          = req.Name;
        e.Description   = req.Description;
        e.EffectiveFrom = req.EffectiveFrom;
        e.EffectiveTo   = req.EffectiveTo;
        e.IsDefault     = req.IsDefault;
        e.Notes         = req.Notes;
        e.Status        = req.Status;
        e.UpdatedAt     = DateTimeOffset.UtcNow;
        e.UpdatedBy     = cmd.ActorId;
        e.Version++;

        await _db.SaveChangesAsync(ct);
        return CreatePriceListHandler.ToDto(e);
    }
}

// ── Publish PriceList ─────────────────────────────────────────────────────────

public sealed record PublishPriceListCommand(Guid Id, Guid? ActorId) : IRequest<PriceListDto?>;

public sealed class PublishPriceListHandler : IRequestHandler<PublishPriceListCommand, PriceListDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public PublishPriceListHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PriceListDto?> Handle(PublishPriceListCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.PriceLists
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return null;

        if (e.IsPublished)
            return CreatePriceListHandler.ToDto(e); // idempotent

        var now = DateTimeOffset.UtcNow;
        e.IsPublished = true;
        e.PublishedAt = now;
        e.PublishedBy = cmd.ActorId;
        e.Status      = "published";
        e.UpdatedAt   = now;
        e.UpdatedBy   = cmd.ActorId;
        e.Version++;

        await _db.SaveChangesAsync(ct);
        return CreatePriceListHandler.ToDto(e);
    }
}

// ── Soft Delete PriceList ─────────────────────────────────────────────────────

public sealed record DeletePriceListCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

public sealed class DeletePriceListHandler : IRequestHandler<DeletePriceListCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public DeletePriceListHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> Handle(DeletePriceListCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.PriceLists
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return false;

        e.DeletedAt = DateTimeOffset.UtcNow;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class CreatePriceListValidator : AbstractValidator<CreatePriceListCommand>
{
    public CreatePriceListValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.Request.ScopeType).NotEmpty().Must(s =>
            s is "brand" or "franchise" or "store")
            .WithMessage("ScopeType must be brand, franchise, or store.");
    }
}
