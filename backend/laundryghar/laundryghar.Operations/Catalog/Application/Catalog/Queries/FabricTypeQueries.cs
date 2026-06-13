using laundryghar.Catalog.Infrastructure.Auth;
using laundryghar.Catalog.Infrastructure.Services;
using laundryghar.Catalog.Application.Catalog.Commands;
using laundryghar.Catalog.Application.Catalog.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Catalog.Application.Catalog.Queries;

public sealed record GetFabricTypesQuery(int Page, int PageSize) : IRequest<PaginatedList<FabricTypeDto>>;

public sealed class GetFabricTypesHandler : IRequestHandler<GetFabricTypesQuery, PaginatedList<FabricTypeDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetFabricTypesHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<FabricTypeDto>> Handle(GetFabricTypesQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        return PaginatedList<FabricTypeDto>.CreateAsync(
            _db.FabricTypes.Where(x => x.BrandId == brandId).OrderBy(x => x.DisplayOrder)
                .Select(x => CreateFabricTypeHandler.ToDto(x)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetFabricTypeByIdQuery(Guid Id) : IRequest<FabricTypeDto?>;

public sealed class GetFabricTypeByIdHandler : IRequestHandler<GetFabricTypeByIdQuery, FabricTypeDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetFabricTypeByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<FabricTypeDto?> Handle(GetFabricTypeByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.FabricTypes
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : CreateFabricTypeHandler.ToDto(e);
    }
}
