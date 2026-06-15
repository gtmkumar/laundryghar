using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Commands.ItemVariant;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Catalog.Queries.ItemVariant;

public sealed record GetItemVariantsQuery(int Page, int PageSize, Guid? ItemId) : IQuery<PaginatedList<ItemVariantDto>>;

public sealed class GetItemVariantsHandler : IQueryHandler<GetItemVariantsQuery, PaginatedList<ItemVariantDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetItemVariantsHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<ItemVariantDto>> HandleAsync(GetItemVariantsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query = _db.ItemVariants.Where(x => x.BrandId == brandId);
        if (q.ItemId.HasValue) query = query.Where(x => x.ItemId == q.ItemId.Value);
        return PaginatedList<ItemVariantDto>.CreateAsync(
            query.OrderBy(x => x.DisplayOrder).Select(x => CreateItemVariantHandler.ToDto(x)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetItemVariantByIdQuery(Guid Id) : IQuery<ItemVariantDto?>;

public sealed class GetItemVariantByIdHandler : IQueryHandler<GetItemVariantByIdQuery, ItemVariantDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetItemVariantByIdHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ItemVariantDto?> HandleAsync(GetItemVariantByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.ItemVariants
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : CreateItemVariantHandler.ToDto(e);
    }
}
