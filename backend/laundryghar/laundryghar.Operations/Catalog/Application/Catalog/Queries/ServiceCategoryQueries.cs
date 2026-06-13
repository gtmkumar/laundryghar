using laundryghar.Catalog.Application.Catalog.Commands;
using laundryghar.Catalog.Application.Catalog.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Catalog.Application.Catalog.Queries;

public sealed record GetServiceCategoriesQuery(int Page, int PageSize) : IRequest<PaginatedList<ServiceCategoryDto>>;

public sealed class GetServiceCategoriesHandler : IRequestHandler<GetServiceCategoriesQuery, PaginatedList<ServiceCategoryDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetServiceCategoriesHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public Task<PaginatedList<ServiceCategoryDto>> Handle(GetServiceCategoriesQuery q, CancellationToken ct)
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

public sealed record GetServiceCategoryByIdQuery(Guid Id) : IRequest<ServiceCategoryDto?>;

public sealed class GetServiceCategoryByIdHandler : IRequestHandler<GetServiceCategoryByIdQuery, ServiceCategoryDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetServiceCategoryByIdHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<ServiceCategoryDto?> Handle(GetServiceCategoryByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.ServiceCategories
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : CreateServiceCategoryHandler.ToDto(e);
    }
}
