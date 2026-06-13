using laundryghar.Finance.Application.Royalty.Commands;
using laundryghar.Finance.Application.Royalty.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

using laundryghar.Finance.Infrastructure.Services;
namespace laundryghar.Finance.Application.Royalty.Queries;

// ── List royalty invoices ─────────────────────────────────────────────────────

public sealed record GetRoyaltyInvoicesQuery(
    int    Page,
    int    PageSize,
    Guid?  FranchiseId,
    string? Status) : IRequest<PaginatedList<RoyaltyInvoiceDto>>;

public sealed class GetRoyaltyInvoicesHandler
    : IRequestHandler<GetRoyaltyInvoicesQuery, PaginatedList<RoyaltyInvoiceDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    public GetRoyaltyInvoicesHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public Task<PaginatedList<RoyaltyInvoiceDto>> Handle(GetRoyaltyInvoicesQuery q, CancellationToken ct)
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

public sealed record GetRoyaltyInvoiceByIdQuery(Guid Id) : IRequest<RoyaltyInvoiceDto?>;

public sealed class GetRoyaltyInvoiceByIdHandler
    : IRequestHandler<GetRoyaltyInvoiceByIdQuery, RoyaltyInvoiceDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    public GetRoyaltyInvoiceByIdHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RoyaltyInvoiceDto?> Handle(GetRoyaltyInvoiceByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var invoice = await _db.RoyaltyInvoices
            .Include(i => i.Calculations)
            .FirstOrDefaultAsync(i => i.Id == q.Id && i.BrandId == brandId, ct);
        return invoice is null ? null : RoyaltyMapper.ToDto(invoice);
    }
}
