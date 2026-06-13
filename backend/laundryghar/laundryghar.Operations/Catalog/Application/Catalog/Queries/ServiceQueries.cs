using laundryghar.Catalog.Infrastructure.Auth;
using laundryghar.Catalog.Infrastructure.Services;
using laundryghar.Catalog.Application.Catalog.Commands;
using laundryghar.Catalog.Application.Catalog.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Catalog.Application.Catalog.Queries;

public sealed record GetServicesQuery(int Page, int PageSize, Guid? CategoryId) : IRequest<PaginatedList<ServiceDto>>;

public sealed class GetServicesHandler : IRequestHandler<GetServicesQuery, PaginatedList<ServiceDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetServicesHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<ServiceDto>> Handle(GetServicesQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        // Brand predicate enforced in-handler (defense-in-depth; RLS also active for non-superuser roles).
        var query = _db.Services.Where(x => x.BrandId == brandId);
        if (q.CategoryId.HasValue) query = query.Where(x => x.CategoryId == q.CategoryId.Value);

        return PaginatedList<ServiceDto>.CreateAsync(
            query.OrderBy(x => x.DisplayOrder).Select(x => CreateServiceHandler.ToDto(x)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetServiceByIdQuery(Guid Id) : IRequest<ServiceDto?>;

public sealed class GetServiceByIdHandler : IRequestHandler<GetServiceByIdQuery, ServiceDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetServiceByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ServiceDto?> Handle(GetServiceByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Services.FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : CreateServiceHandler.ToDto(e);
    }
}
