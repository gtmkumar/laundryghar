using laundryghar.Catalog.Infrastructure.Auth;
using laundryghar.Catalog.Infrastructure.Services;
using laundryghar.Catalog.Application.Catalog.Commands;
using laundryghar.Catalog.Application.Catalog.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Catalog.Application.Catalog.Queries;

public sealed record GetItemVariantsQuery(int Page, int PageSize, Guid? ItemId) : IRequest<PaginatedList<ItemVariantDto>>;

public sealed class GetItemVariantsHandler : IRequestHandler<GetItemVariantsQuery, PaginatedList<ItemVariantDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetItemVariantsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<ItemVariantDto>> Handle(GetItemVariantsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query = _db.ItemVariants.Where(x => x.BrandId == brandId);
        if (q.ItemId.HasValue) query = query.Where(x => x.ItemId == q.ItemId.Value);
        return PaginatedList<ItemVariantDto>.CreateAsync(
            query.OrderBy(x => x.DisplayOrder).Select(x => CreateItemVariantHandler.ToDto(x)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetItemVariantByIdQuery(Guid Id) : IRequest<ItemVariantDto?>;

public sealed class GetItemVariantByIdHandler : IRequestHandler<GetItemVariantByIdQuery, ItemVariantDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetItemVariantByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ItemVariantDto?> Handle(GetItemVariantByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.ItemVariants
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : CreateItemVariantHandler.ToDto(e);
    }
}
