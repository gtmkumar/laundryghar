using FluentValidation;
using laundryghar.Catalog.Application.Catalog.Dtos;
using MediatR;

namespace laundryghar.Catalog.Application.Catalog.Commands;

public sealed record CreateItemCommand(CreateItemRequest Request, Guid? ActorId) : IRequest<ItemDto>;

public sealed class CreateItemHandler : IRequestHandler<CreateItemCommand, ItemDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateItemHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<ItemDto> Handle(CreateItemCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var e = new Item
        {
            Id                    = Guid.NewGuid(),
            BrandId               = brandId,
            ItemGroupId           = req.ItemGroupId,
            Code                  = req.Code,
            Name                  = req.Name,
            NameLocalized         = req.NameLocalized,
            Description           = req.Description,
            IconUrl               = req.IconUrl,
            ImageUrl              = req.ImageUrl,
            TypicalWeightGrams    = req.TypicalWeightGrams,
            RequiresPerSidePrice  = req.RequiresPerSidePrice,
            // SearchTokens is DB-managed (tsvector) — do NOT write it
            Aliases               = req.Aliases ?? [],
            DisplayOrder          = req.DisplayOrder,
            Status                = "active",
            CreatedAt             = now,
            UpdatedAt             = now,
            CreatedBy             = cmd.ActorId,
            UpdatedBy             = cmd.ActorId,
            Version               = 1
        };

        _db.Items.Add(e);
        await _db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    internal static ItemDto ToDto(Item e) => new(
        e.Id, e.BrandId, e.ItemGroupId, e.Code, e.Name, e.NameLocalized,
        e.Description, e.IconUrl, e.ImageUrl, e.TypicalWeightGrams,
        e.RequiresPerSidePrice, e.Aliases, e.DisplayOrder, e.Status, e.CreatedAt, e.UpdatedAt);
}

public sealed record UpdateItemCommand(Guid Id, UpdateItemRequest Request, Guid? ActorId) : IRequest<ItemDto?>;

public sealed class UpdateItemHandler : IRequestHandler<UpdateItemCommand, ItemDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateItemHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ItemDto?> Handle(UpdateItemCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Items
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return null;

        var req = cmd.Request;
        e.ItemGroupId         = req.ItemGroupId;
        e.Name                = req.Name;
        e.NameLocalized       = req.NameLocalized;
        e.Description         = req.Description;
        e.IconUrl             = req.IconUrl;
        e.ImageUrl            = req.ImageUrl;
        e.TypicalWeightGrams  = req.TypicalWeightGrams;
        e.RequiresPerSidePrice = req.RequiresPerSidePrice;
        e.Aliases             = req.Aliases ?? [];
        e.DisplayOrder        = req.DisplayOrder;
        e.Status              = req.Status;
        e.UpdatedAt           = DateTimeOffset.UtcNow;
        e.UpdatedBy           = cmd.ActorId;
        e.Version++;

        await _db.SaveChangesAsync(ct);
        return CreateItemHandler.ToDto(e);
    }
}

public sealed record DeleteItemCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

public sealed class DeleteItemHandler : IRequestHandler<DeleteItemCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteItemHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> Handle(DeleteItemCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Items
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return false;

        e.DeletedAt = DateTimeOffset.UtcNow;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class CreateItemValidator : AbstractValidator<CreateItemCommand>
{
    public CreateItemValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.NameLocalized).NotEmpty().MaximumLength(200);
    }
}
