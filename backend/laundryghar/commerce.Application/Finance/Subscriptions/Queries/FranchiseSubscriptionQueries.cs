using commerce.Application.Common.Interfaces;
using commerce.Application.Finance.Subscriptions.Dtos;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty.Subscriptions;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Finance.Subscriptions.Queries;

public sealed record GetFranchiseSubscriptionsQuery(int Page, int PageSize, Guid? FranchiseId, string? Status)
    : IQuery<PaginatedList<FranchiseSubscriptionDto>>;

public sealed class GetFranchiseSubscriptionsHandler
    : IQueryHandler<GetFranchiseSubscriptionsQuery, PaginatedList<FranchiseSubscriptionDto>>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser       _user;

    public GetFranchiseSubscriptionsHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<FranchiseSubscriptionDto>> HandleAsync(GetFranchiseSubscriptionsQuery q, CancellationToken ct)
    {
        IQueryable<FranchiseSubscription> query;

        if (_user.IsPlatformAdmin)
        {
            // Platform admin: can view all brands (worker bypasses RLS too)
            query = _db.FranchiseSubscriptions.AsQueryable();
        }
        else
        {
            var brandId = _user.RequireBrandId();
            query = _db.FranchiseSubscriptions.Where(fs => fs.BrandId == brandId);
        }

        if (q.FranchiseId.HasValue)       query = query.Where(fs => fs.FranchiseId == q.FranchiseId.Value);
        if (!string.IsNullOrEmpty(q.Status)) query = query.Where(fs => fs.Status == q.Status);

        return PaginatedList<FranchiseSubscriptionDto>.CreateAsync(
            query.OrderByDescending(fs => fs.CreatedAt).Select(fs => ToDto(fs)),
            q.Page, q.PageSize, ct);
    }

    internal static FranchiseSubscriptionDto ToDto(FranchiseSubscription fs) => new(
        fs.Id, fs.BrandId, fs.FranchiseId, fs.PlatformPlanId,
        fs.SubscriptionNumber, fs.PriceSnapshot, fs.BillingInterval,
        fs.Status, fs.AutoRenew, fs.CurrentPeriodStart, fs.CurrentPeriodEnd,
        fs.NextBillingAt, fs.DunningAttempts, fs.SuspendedAt,
        fs.TotalCyclesBilled, fs.CreatedAt, fs.UpdatedAt);
}
