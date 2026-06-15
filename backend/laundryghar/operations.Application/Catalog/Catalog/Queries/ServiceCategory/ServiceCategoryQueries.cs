using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Commands.ServiceCategory;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Catalog.Queries.ServiceCategory;

public sealed record GetServiceCategoriesQuery(int Page, int PageSize) : IQuery<PaginatedList<ServiceCategoryDto>>;

public sealed class GetServiceCategoriesHandler : IQueryHandler<GetServiceCategoriesQuery, PaginatedList<ServiceCategoryDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetServiceCategoriesHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public Task<PaginatedList<ServiceCategoryDto>> HandleAsync(GetServiceCategoriesQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        // Brand predicate enforced in-handler (defense-in-depth; RLS also active for non-superuser roles).
        var query = _db.ServiceCategories
            .Where(x => x.BrandId == brandId)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => CreateServiceCategoryHandler.ToDto(x));

        return PaginatedList<ServiceCategoryDto>.CreateAsync(query, q.Page, q.PageSize, ct);
    }
}

public sealed record GetServiceCategoryByIdQuery(Guid Id) : IQuery<ServiceCategoryDto?>;

public sealed class GetServiceCategoryByIdHandler : IQueryHandler<GetServiceCategoryByIdQuery, ServiceCategoryDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetServiceCategoryByIdHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<ServiceCategoryDto?> HandleAsync(GetServiceCategoryByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.ServiceCategories
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : CreateServiceCategoryHandler.ToDto(e);
    }
}
