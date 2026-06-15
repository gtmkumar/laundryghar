using commerce.Application.Common.Interfaces;
using commerce.Application.Finance.Subscriptions.Dtos;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty.Subscriptions;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Finance.Subscriptions.Queries;

// ── Queries ───────────────────────────────────────────────────────────────────

public sealed record GetPlatformPlansQuery(int Page, int PageSize, string? Status)
    : IQuery<PaginatedList<PlatformPlanDto>>;

public sealed class GetPlatformPlansHandler : IQueryHandler<GetPlatformPlansQuery, PaginatedList<PlatformPlanDto>>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;

    public GetPlatformPlansHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<PlatformPlanDto>> HandleAsync(GetPlatformPlansQuery q, CancellationToken ct)
    {
        // Platform admins can see all plans (global + brand-specific).
        // Brand admins can only see global plans and their brand's plans.
        var query = _db.PlatformPlans.AsQueryable();
        if (!_user.IsPlatformAdmin)
        {
            var brandId = _user.RequireBrandId();
            query = query.Where(p => p.BrandId == null || p.BrandId == brandId);
        }

        if (!string.IsNullOrEmpty(q.Status)) query = query.Where(p => p.Status == q.Status);

        return PaginatedList<PlatformPlanDto>.CreateAsync(
            query.OrderBy(p => p.DisplayOrder).ThenBy(p => p.Price).Select(p => ToDto(p)),
            q.Page, q.PageSize, ct);
    }

    internal static PlatformPlanDto ToDto(PlatformPlan p) => new(
        p.Id, p.BrandId, p.Code, p.Name, p.Description, p.Tier,
        p.BillingInterval, p.IntervalCount, p.Price, p.SetupFee,
        p.AnnualDiscountPercent, p.CurrencyCode, p.TrialDays,
        p.MaxStores, p.MaxWarehouses, p.MaxUsers, p.MaxOrdersPerMonth, p.MaxRiders,
        p.OveragePerOrder, p.OveragePerStore, p.OveragePerUser,
        p.Features, p.SupportLevel, p.IsPublic, p.IsFeatured, p.DisplayOrder,
        p.Status, p.CreatedAt, p.UpdatedAt);
}

public sealed record GetPlatformPlanByIdQuery(Guid Id) : IQuery<PlatformPlanDto?>;

public sealed class GetPlatformPlanByIdHandler : IQueryHandler<GetPlatformPlanByIdQuery, PlatformPlanDto?>
{
    private readonly ICommerceDbContext _db;

    public GetPlatformPlanByIdHandler(ICommerceDbContext db) => _db = db;

    public async Task<PlatformPlanDto?> HandleAsync(GetPlatformPlanByIdQuery q, CancellationToken ct)
    {
        var e = await _db.PlatformPlans.FirstOrDefaultAsync(p => p.Id == q.Id && p.DeletedAt == null, ct);
        return e is null ? null : GetPlatformPlansHandler.ToDto(e);
    }
}
