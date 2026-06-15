using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Commands.ItemGroup;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Catalog.Queries.ItemGroup;

public sealed record GetItemGroupsQuery(int Page, int PageSize) : IQuery<PaginatedList<ItemGroupDto>>;

public sealed class GetItemGroupsHandler : IQueryHandler<GetItemGroupsQuery, PaginatedList<ItemGroupDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetItemGroupsHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<ItemGroupDto>> HandleAsync(GetItemGroupsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        return PaginatedList<ItemGroupDto>.CreateAsync(
            _db.ItemGroups.Where(x => x.BrandId == brandId).OrderBy(x => x.DisplayOrder)
                .Select(x => CreateItemGroupHandler.ToDto(x)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetItemGroupByIdQuery(Guid Id) : IQuery<ItemGroupDto?>;

public sealed class GetItemGroupByIdHandler : IQueryHandler<GetItemGroupByIdQuery, ItemGroupDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetItemGroupByIdHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ItemGroupDto?> HandleAsync(GetItemGroupByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.ItemGroups
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : CreateItemGroupHandler.ToDto(e);
    }
}
