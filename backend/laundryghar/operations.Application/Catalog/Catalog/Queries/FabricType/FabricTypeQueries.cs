using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Commands.FabricType;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Catalog.Queries.FabricType;

public sealed record GetFabricTypesQuery(int Page, int PageSize) : IQuery<PaginatedList<FabricTypeDto>>;

public sealed class GetFabricTypesHandler : IQueryHandler<GetFabricTypesQuery, PaginatedList<FabricTypeDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetFabricTypesHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<FabricTypeDto>> HandleAsync(GetFabricTypesQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        return PaginatedList<FabricTypeDto>.CreateAsync(
            _db.FabricTypes.Where(x => x.BrandId == brandId).OrderBy(x => x.DisplayOrder)
                .Select(x => CreateFabricTypeHandler.ToDto(x)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetFabricTypeByIdQuery(Guid Id) : IQuery<FabricTypeDto?>;

public sealed class GetFabricTypeByIdHandler : IQueryHandler<GetFabricTypeByIdQuery, FabricTypeDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetFabricTypeByIdHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<FabricTypeDto?> HandleAsync(GetFabricTypeByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.FabricTypes
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : CreateFabricTypeHandler.ToDto(e);
    }
}
