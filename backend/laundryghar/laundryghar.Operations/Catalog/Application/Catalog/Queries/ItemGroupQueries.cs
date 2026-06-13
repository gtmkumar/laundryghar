using laundryghar.Catalog.Application.Catalog.Commands;
using laundryghar.Catalog.Application.Catalog.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Catalog.Application.Catalog.Queries;

public sealed record GetItemGroupsQuery(int Page, int PageSize) : IRequest<PaginatedList<ItemGroupDto>>;

public sealed class GetItemGroupsHandler : IRequestHandler<GetItemGroupsQuery, PaginatedList<ItemGroupDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetItemGroupsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<ItemGroupDto>> Handle(GetItemGroupsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        return PaginatedList<ItemGroupDto>.CreateAsync(
            _db.ItemGroups.Where(x => x.BrandId == brandId).OrderBy(x => x.DisplayOrder)
                .Select(x => CreateItemGroupHandler.ToDto(x)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetItemGroupByIdQuery(Guid Id) : IRequest<ItemGroupDto?>;

public sealed class GetItemGroupByIdHandler : IRequestHandler<GetItemGroupByIdQuery, ItemGroupDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetItemGroupByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ItemGroupDto?> Handle(GetItemGroupByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.ItemGroups
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : CreateItemGroupHandler.ToDto(e);
    }
}
