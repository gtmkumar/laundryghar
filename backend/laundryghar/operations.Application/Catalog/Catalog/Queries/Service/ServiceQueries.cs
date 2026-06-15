using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Commands.Service;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Catalog.Queries.Service;

public sealed record GetServicesQuery(int Page, int PageSize, Guid? CategoryId) : IQuery<PaginatedList<ServiceDto>>;

public sealed class GetServicesHandler : IQueryHandler<GetServicesQuery, PaginatedList<ServiceDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetServicesHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<ServiceDto>> HandleAsync(GetServicesQuery q, CancellationToken ct)
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

public sealed record GetServiceByIdQuery(Guid Id) : IQuery<ServiceDto?>;

public sealed class GetServiceByIdHandler : IQueryHandler<GetServiceByIdQuery, ServiceDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetServiceByIdHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ServiceDto?> HandleAsync(GetServiceByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Services.FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : CreateServiceHandler.ToDto(e);
    }
}
