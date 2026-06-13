using laundryghar.Catalog.Infrastructure.Auth;
using laundryghar.Catalog.Infrastructure.Services;
using laundryghar.Catalog.Application.Catalog.Commands;
using laundryghar.Catalog.Application.Catalog.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Catalog.Application.Catalog.Queries;

public sealed record GetAddOnsQuery(int Page, int PageSize) : IRequest<PaginatedList<AddOnDto>>;

public sealed class GetAddOnsHandler : IRequestHandler<GetAddOnsQuery, PaginatedList<AddOnDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetAddOnsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<AddOnDto>> Handle(GetAddOnsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        return PaginatedList<AddOnDto>.CreateAsync(
            _db.AddOns.Where(x => x.BrandId == brandId).OrderBy(x => x.DisplayOrder)
                .Select(x => CreateAddOnHandler.ToDto(x)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetAddOnByIdQuery(Guid Id) : IRequest<AddOnDto?>;

public sealed class GetAddOnByIdHandler : IRequestHandler<GetAddOnByIdQuery, AddOnDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetAddOnByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<AddOnDto?> Handle(GetAddOnByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.AddOns
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : CreateAddOnHandler.ToDto(e);
    }
}
