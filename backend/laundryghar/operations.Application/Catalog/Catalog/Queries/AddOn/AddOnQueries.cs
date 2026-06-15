using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Commands.AddOn;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Catalog.Queries.AddOn;

public sealed record GetAddOnsQuery(int Page, int PageSize) : IQuery<PaginatedList<AddOnDto>>;

public sealed class GetAddOnsHandler : IQueryHandler<GetAddOnsQuery, PaginatedList<AddOnDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetAddOnsHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<AddOnDto>> HandleAsync(GetAddOnsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        return PaginatedList<AddOnDto>.CreateAsync(
            _db.AddOns.Where(x => x.BrandId == brandId).OrderBy(x => x.DisplayOrder)
                .Select(x => CreateAddOnHandler.ToDto(x)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetAddOnByIdQuery(Guid Id) : IQuery<AddOnDto?>;

public sealed class GetAddOnByIdHandler : IQueryHandler<GetAddOnByIdQuery, AddOnDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetAddOnByIdHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<AddOnDto?> HandleAsync(GetAddOnByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.AddOns
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : CreateAddOnHandler.ToDto(e);
    }
}
