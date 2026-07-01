using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Auth.Audit;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Pricing.Dtos;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Pricing.Commands.PriceList;

// ── Create PriceList ──────────────────────────────────────────────────────────

public sealed record CreatePriceListCommand(CreatePriceListRequest Request, Guid? ActorId) : ICommand<PriceListDto>;

public sealed class CreatePriceListHandler : ICommandHandler<CreatePriceListCommand, PriceListDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreatePriceListHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<PriceListDto> HandleAsync(CreatePriceListCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;

        if (!_user.IsWithinScope(brandId: brandId, franchiseId: req.FranchiseId, storeId: req.StoreId))
            throw new ForbiddenException("This price list is outside your assigned scope.");

        var now = DateTimeOffset.UtcNow;

        // Business version: start at 1
        var e = new laundryghar.SharedDataModel.Entities.CustomerCatalog.PriceList
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

    internal static PriceListDto ToDto(laundryghar.SharedDataModel.Entities.CustomerCatalog.PriceList e) => new(
        e.Id, e.BrandId, e.FranchiseId, e.StoreId, e.Code, e.Name, e.Description,
        e.CurrencyCode, e.ScopeType, e.VersionNumber, e.ParentPriceListId,
        e.EffectiveFrom, e.EffectiveTo, e.IsDefault, e.IsPublished, e.PublishedAt,
        e.Status, e.Notes, e.CreatedAt, e.UpdatedAt);
}

// ── Update PriceList ──────────────────────────────────────────────────────────

public sealed record UpdatePriceListCommand(Guid Id, UpdatePriceListRequest Request, Guid? ActorId) : ICommand<PriceListDto?>;

public sealed class UpdatePriceListHandler : ICommandHandler<UpdatePriceListCommand, PriceListDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public UpdatePriceListHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PriceListDto?> HandleAsync(UpdatePriceListCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.PriceLists
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return null;

        if (!_user.IsWithinScope(brandId: e.BrandId, franchiseId: e.FranchiseId, storeId: e.StoreId))
            throw new ForbiddenException("This price list is outside your assigned scope.");

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

public sealed record PublishPriceListCommand(Guid Id, Guid? ActorId) : ICommand<PriceListDto?>;

public sealed class PublishPriceListHandler : ICommandHandler<PublishPriceListCommand, PriceListDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IAuditWriter _audit;

    public PublishPriceListHandler(IOperationsDbContext db, ICurrentUser user, IAuditWriter audit)
    { _db = db; _user = user; _audit = audit; }

    public async Task<PriceListDto?> HandleAsync(PublishPriceListCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.PriceLists
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return null;

        if (!_user.IsWithinScope(brandId: e.BrandId, franchiseId: e.FranchiseId, storeId: e.StoreId))
            throw new ForbiddenException("This price list is outside your assigned scope.");

        if (e.IsPublished)
            return CreatePriceListHandler.ToDto(e); // idempotent

        var prevStatus = e.Status;
        var now = DateTimeOffset.UtcNow;
        e.IsPublished = true;
        e.PublishedAt = now;
        e.PublishedBy = cmd.ActorId;
        e.Status      = "published";
        e.UpdatedAt   = now;
        e.UpdatedBy   = cmd.ActorId;
        e.Version++;

        await _db.SaveChangesAsync(ct);

        // Semantic audit: publishing flips a whole price list (and its items) live; record it
        // as one named action with the status transition rather than a bare "price_lists.updated".
        await _audit.WriteAsync("pricing.pricelist.publish", "price_lists", e.Id,
            resourceDisplay: $"{e.Code} · {e.Name}",
            oldValues: new { Status = prevStatus, IsPublished = false },
            newValues: new { Status = "published", IsPublished = true },
            changedFields: ["status", "is_published", "published_at"], ct: ct);

        return CreatePriceListHandler.ToDto(e);
    }
}

// ── Soft Delete PriceList ─────────────────────────────────────────────────────

public sealed record DeletePriceListCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public sealed class DeletePriceListHandler : ICommandHandler<DeletePriceListCommand, bool>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public DeletePriceListHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> HandleAsync(DeletePriceListCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.PriceLists
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return false;

        if (!_user.IsWithinScope(brandId: e.BrandId, franchiseId: e.FranchiseId, storeId: e.StoreId))
            throw new ForbiddenException("This price list is outside your assigned scope.");

        // Soft-delete must also move status off the live ('draft'/'published') states so
        // status-keyed reports don't miscount it. price_lists CHECK is
        // ('draft','published','archived') — 'archived' is the terminal state.
        e.Status    = "archived";
        e.DeletedAt = DateTimeOffset.UtcNow;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class CreatePriceListValidator : AbstractValidator<CreatePriceListRequest>
{
    public CreatePriceListValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.ScopeType).NotEmpty().Must(s =>
            s is "brand" or "franchise" or "store")
            .WithMessage("ScopeType must be brand, franchise, or store.");
    }
}
