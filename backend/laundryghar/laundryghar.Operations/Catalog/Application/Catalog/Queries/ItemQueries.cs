using laundryghar.Catalog.Infrastructure.Auth;
using laundryghar.Catalog.Infrastructure.Services;
using laundryghar.Catalog.Application.Catalog.Commands;
using laundryghar.Catalog.Application.Catalog.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Catalog.Application.Catalog.Queries;

public sealed record GetItemsQuery(int Page, int PageSize, Guid? ItemGroupId) : IRequest<PaginatedList<ItemDto>>;

public sealed class GetItemsHandler : IRequestHandler<GetItemsQuery, PaginatedList<ItemDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetItemsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<ItemDto>> Handle(GetItemsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query = _db.Items.Where(x => x.BrandId == brandId);
        if (q.ItemGroupId.HasValue) query = query.Where(x => x.ItemGroupId == q.ItemGroupId.Value);
        return PaginatedList<ItemDto>.CreateAsync(
            query.OrderBy(x => x.DisplayOrder).Select(x => CreateItemHandler.ToDto(x)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetItemByIdQuery(Guid Id) : IRequest<ItemDto?>;

public sealed class GetItemByIdHandler : IRequestHandler<GetItemByIdQuery, ItemDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetItemByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ItemDto?> Handle(GetItemByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Items
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : CreateItemHandler.ToDto(e);
    }
}
