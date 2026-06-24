using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using operations.Application.Common.Interfaces;
using operations.Application.Orders.Common;
using operations.Application.Orders.Orders.Dtos;

namespace operations.Application.Orders.Ops;

/// <summary>
/// Returns three paged ops queue buckets in a single response:
/// <list type="bullet">
///   <item><term>dueToday</term><description>Non-terminal orders whose promised_delivery_at falls on today (brand local-date is approximated as UTC today).</description></item>
///   <item><term>overdue</term><description>Non-terminal orders whose promised_delivery_at is in the past.</description></item>
///   <item><term>stuck</term><description>Non-terminal orders whose last order_status_history entry is older than <see cref="OrdersSettings.StuckThresholdHours"/> hours.</description></item>
/// </list>
/// Each bucket is independently pageable via <paramref name="Page"/>/<paramref name="PageSize"/>.
/// For badge counts the count is always the full filtered count regardless of the page.
/// </summary>
public sealed record OpsQueuesQuery(
    int Page,
    int PageSize,
    Guid? StoreId
) : IQuery<OpsQueuesResponse>;

public sealed class OpsQueuesHandler : IQueryHandler<OpsQueuesQuery, OpsQueuesResponse>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    private readonly OrdersSettings _settings;

    public OpsQueuesHandler(
        IOperationsDbContext db,
        ICurrentUser user,
        IOptions<OrdersSettings> opts)
    {
        _db       = db;
        _user     = user;
        _settings = opts.Value;
    }

    public async Task<OpsQueuesResponse> HandleAsync(OpsQueuesQuery q, CancellationToken ct)
    {
        var brandId  = _user.RequireBrandId();
        var now      = DateTimeOffset.UtcNow;
        var page     = q.Page < 1 ? 1 : q.Page;
        var pageSize = q.PageSize is < 1 or > 100 ? 20 : q.PageSize;

        // Shared base: open (non-terminal) orders for this brand. Filtered on the generic
        // lifecycle super-state (vertical-neutral) rather than a hardcoded laundry-status list —
        // terminal ⟺ lifecycle_state ∈ {completed, cancelled, closed}.
        var baseQuery = _db.Orders
            .Where(o => o.BrandId == brandId && !OrderLifecycleState.Terminal.Contains(o.LifecycleState));

        if (q.StoreId.HasValue)
            baseQuery = baseQuery.Where(o => o.StoreId == q.StoreId.Value);

        // ── Due today ────────────────────────────────────────────────────────
        // promised_delivery_at falls within today UTC (midnight to midnight).
        // Use DateTimeOffset throughout so EF/Npgsql maps to timestamptz correctly.
        var todayStart = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var todayEnd   = todayStart.AddDays(1);

        var dueTodayQuery = baseQuery
            .Where(o => o.PromisedDeliveryAt.HasValue
                     && o.PromisedDeliveryAt.Value >= todayStart
                     && o.PromisedDeliveryAt.Value < todayEnd)
            .OrderBy(o => o.PromisedDeliveryAt);

        var dueTodayTotal = await dueTodayQuery.CountAsync(ct);
        var dueTodayRows  = await dueTodayQuery
            .Join(_db.Customers,
                  o => o.CustomerId,
                  c => c.Id,
                  (o, c) => new
                  {
                      o.Id, o.CreatedAt, o.OrderNumber, o.Status, o.StoreId,
                      o.PromisedDeliveryAt,
                      CustomerName = (c.DisplayName ?? (c.FirstName + " " + c.LastName)).Trim()
                  })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // ── Overdue ──────────────────────────────────────────────────────────
        // promised_delivery_at is strictly before now (not terminal).
        var overdueQuery = baseQuery
            .Where(o => o.PromisedDeliveryAt.HasValue
                     && o.PromisedDeliveryAt.Value < now)
            .OrderBy(o => o.PromisedDeliveryAt);   // oldest first = most urgent

        var overdueTotal = await overdueQuery.CountAsync(ct);
        var overdueRows  = await overdueQuery
            .Join(_db.Customers,
                  o => o.CustomerId,
                  c => c.Id,
                  (o, c) => new
                  {
                      o.Id, o.CreatedAt, o.OrderNumber, o.Status, o.StoreId,
                      o.PromisedDeliveryAt,
                      CustomerName = (c.DisplayName ?? (c.FirstName + " " + c.LastName)).Trim()
                  })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // ── Unactioned ("needs action") ──────────────────────────────────────
        // Freshly created orders nobody has actioned yet (laundry: still 'placed', pickup
        // not scheduled). Generic lifecycle super-state 'created' is the vertical-neutral
        // equivalent (created ⟺ the mode's initial status).
        // Oldest first so the most-aged (most urgent) bubble to the top.
        var unactionedQuery = baseQuery
            .Where(o => o.LifecycleState == OrderLifecycleState.Created)
            .OrderBy(o => o.CreatedAt);

        var unactionedTotal = await unactionedQuery.CountAsync(ct);
        var unactionedRows  = await unactionedQuery
            .Join(_db.Customers,
                  o => o.CustomerId,
                  c => c.Id,
                  (o, c) => new
                  {
                      o.Id, o.CreatedAt, o.OrderNumber, o.Status, o.StoreId,
                      o.PromisedDeliveryAt,
                      CustomerName = (c.DisplayName ?? (c.FirstName + " " + c.LastName)).Trim()
                  })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // ── Stuck ────────────────────────────────────────────────────────────
        // Non-terminal order whose LATEST status_history entry is older than StuckThresholdHours.
        // We compute the threshold cutoff and find orders with no history newer than it.
        var stuckCutoff = now.AddHours(-_settings.StuckThresholdHours);

        // Sub-select: max changed_at per order_id
        var latestHistory = _db.OrderStatusHistories
            .GroupBy(h => h.OrderId)
            .Select(g => new { OrderId = g.Key, LastChanged = g.Max(h => h.ChangedAt) });

        var stuckQuery = baseQuery
            .Join(latestHistory,
                  o => o.Id,
                  lh => lh.OrderId,
                  (o, lh) => new { Order = o, lh.LastChanged })
            .Where(x => x.LastChanged < stuckCutoff)
            .OrderBy(x => x.LastChanged);   // longest-stuck first

        var stuckTotal = await stuckQuery.CountAsync(ct);
        var stuckRows  = await stuckQuery
            .Join(_db.Customers,
                  x => x.Order.CustomerId,
                  c => c.Id,
                  (x, c) => new
                  {
                      x.Order.Id,
                      x.Order.CreatedAt,
                      x.Order.OrderNumber,
                      x.Order.Status,
                      x.Order.StoreId,
                      x.Order.PromisedDeliveryAt,
                      x.LastChanged,
                      CustomerName = (c.DisplayName ?? (c.FirstName + " " + c.LastName)).Trim()
                  })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // ── Project ──────────────────────────────────────────────────────────

        static OpsOrderDto ToOpsDto(
            Guid id, DateTimeOffset createdAt, string orderNumber, string customerName,
            string status, DateTimeOffset? promisedDeliveryAt, Guid storeId,
            DateTimeOffset now, DateTimeOffset? lastChanged = null)
        {
            double? hoursOverdue = promisedDeliveryAt.HasValue && promisedDeliveryAt.Value < now
                ? (now - promisedDeliveryAt.Value).TotalHours
                : null;

            double? hoursStuck = lastChanged.HasValue
                ? (now - lastChanged.Value).TotalHours
                : null;

            double ageMinutes = Math.Max(0, (now - createdAt).TotalMinutes);

            return new OpsOrderDto(id, createdAt, orderNumber, customerName,
                status, promisedDeliveryAt, hoursOverdue, hoursStuck, storeId, ageMinutes);
        }

        var dueTodayBucket = new OpsQueueBucket(
            Count: dueTodayTotal,
            List: dueTodayRows.Select(r => ToOpsDto(
                r.Id, r.CreatedAt, r.OrderNumber, r.CustomerName,
                r.Status, r.PromisedDeliveryAt, r.StoreId, now)).ToList(),
            HasNextPage: (page * pageSize) < dueTodayTotal,
            TotalCount: dueTodayTotal);

        var overdueBucket = new OpsQueueBucket(
            Count: overdueTotal,
            List: overdueRows.Select(r => ToOpsDto(
                r.Id, r.CreatedAt, r.OrderNumber, r.CustomerName,
                r.Status, r.PromisedDeliveryAt, r.StoreId, now)).ToList(),
            HasNextPage: (page * pageSize) < overdueTotal,
            TotalCount: overdueTotal);

        var stuckBucket = new OpsQueueBucket(
            Count: stuckTotal,
            List: stuckRows.Select(r => ToOpsDto(
                r.Id, r.CreatedAt, r.OrderNumber, r.CustomerName,
                r.Status, r.PromisedDeliveryAt, r.StoreId, now, r.LastChanged)).ToList(),
            HasNextPage: (page * pageSize) < stuckTotal,
            TotalCount: stuckTotal);

        var unactionedBucket = new OpsQueueBucket(
            Count: unactionedTotal,
            List: unactionedRows.Select(r => ToOpsDto(
                r.Id, r.CreatedAt, r.OrderNumber, r.CustomerName,
                r.Status, r.PromisedDeliveryAt, r.StoreId, now)).ToList(),
            HasNextPage: (page * pageSize) < unactionedTotal,
            TotalCount: unactionedTotal);

        return new OpsQueuesResponse(dueTodayBucket, overdueBucket, stuckBucket, unactionedBucket);
    }
}
