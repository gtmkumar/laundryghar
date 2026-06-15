using commerce.Application.Common.Interfaces;
using commerce.Application.Finance.Royalty.Commands;
using commerce.Application.Finance.Royalty.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Finance.Royalty.Queries;

// ── List royalty invoices ─────────────────────────────────────────────────────

public sealed record GetRoyaltyInvoicesQuery(
    int    Page,
    int    PageSize,
    Guid?  FranchiseId,
    string? Status) : IQuery<PaginatedList<RoyaltyInvoiceDto>>;

public sealed class GetRoyaltyInvoicesHandler
    : IQueryHandler<GetRoyaltyInvoicesQuery, PaginatedList<RoyaltyInvoiceDto>>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;
    public GetRoyaltyInvoicesHandler(ICommerceDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public Task<PaginatedList<RoyaltyInvoiceDto>> HandleAsync(GetRoyaltyInvoicesQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.RoyaltyInvoices
            .Include(i => i.Calculations)
            .Where(i => i.BrandId == brandId);

        if (q.FranchiseId.HasValue) query = query.Where(i => i.FranchiseId == q.FranchiseId.Value);
        if (!string.IsNullOrEmpty(q.Status)) query = query.Where(i => i.Status == q.Status);

        return PaginatedList<RoyaltyInvoiceDto>.CreateAsync(
            query.OrderByDescending(i => i.PeriodStart)
                .Select(i => RoyaltyMapper.ToDto(i)),
            q.Page, q.PageSize, ct);
    }
}

// ── Get royalty invoice by id ─────────────────────────────────────────────────

public sealed record GetRoyaltyInvoiceByIdQuery(Guid Id) : IQuery<RoyaltyInvoiceDto?>;

public sealed class GetRoyaltyInvoiceByIdHandler
    : IQueryHandler<GetRoyaltyInvoiceByIdQuery, RoyaltyInvoiceDto?>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;
    public GetRoyaltyInvoiceByIdHandler(ICommerceDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RoyaltyInvoiceDto?> HandleAsync(GetRoyaltyInvoiceByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var invoice = await _db.RoyaltyInvoices
            .Include(i => i.Calculations)
            .FirstOrDefaultAsync(i => i.Id == q.Id && i.BrandId == brandId, ct);
        return invoice is null ? null : RoyaltyMapper.ToDto(invoice);
    }
}
