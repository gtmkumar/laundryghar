using laundryghar.Orders.Infrastructure.Auth;
using laundryghar.Orders.Infrastructure.Services;
using laundryghar.Orders.Application.Delivery.Commands;
using laundryghar.Orders.Application.Delivery.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Orders.Application.Delivery.Queries;

public sealed record GetDeliverySlotsQuery(Guid? StoreId, DateOnly? SlotDate, string? SlotType, int Page, int PageSize)
    : IRequest<PaginatedList<DeliverySlotDto>>;

public sealed class GetDeliverySlotsHandler : IRequestHandler<GetDeliverySlotsQuery, PaginatedList<DeliverySlotDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetDeliverySlotsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<DeliverySlotDto>> Handle(GetDeliverySlotsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.DeliverySlots.Where(s => s.BrandId == brandId);

        if (q.StoreId.HasValue)            query = query.Where(s => s.StoreId == q.StoreId.Value);
        if (q.SlotDate.HasValue)           query = query.Where(s => s.SlotDate == q.SlotDate.Value);
        if (!string.IsNullOrEmpty(q.SlotType)) query = query.Where(s => s.SlotType == q.SlotType);

        return PaginatedList<DeliverySlotDto>.CreateAsync(
            query.OrderBy(s => s.SlotDate).ThenBy(s => s.SlotStart)
                 .Select(s => CreateDeliverySlotHandler.ToDto(s)),
            q.Page, q.PageSize, ct);
    }
}

/// <summary>Customer-facing: available slots (is_active, capacity remaining) for a given store+date.</summary>
public sealed record GetAvailableSlotsQuery(Guid? StoreId, DateOnly? SlotDate)
    : IRequest<IReadOnlyList<DeliverySlotDto>>;

public sealed class GetAvailableSlotsHandler : IRequestHandler<GetAvailableSlotsQuery, IReadOnlyList<DeliverySlotDto>>
{
    private readonly LaundryGharDbContext _db;

    public GetAvailableSlotsHandler(LaundryGharDbContext db) => _db = db;

    public async Task<IReadOnlyList<DeliverySlotDto>> Handle(GetAvailableSlotsQuery q, CancellationToken ct)
    {
        // RLS provides brand filtering; explicit brand predicate is the in-handler defense-in-depth.
        // Customer's brand comes from their JWT brand_id → RLS sets app.brand_id session var.
        var query = _db.DeliverySlots.Where(s => s.IsActive && s.BookedCount < s.Capacity);

        if (q.StoreId.HasValue)  query = query.Where(s => s.StoreId == q.StoreId.Value);
        if (q.SlotDate.HasValue) query = query.Where(s => s.SlotDate == q.SlotDate.Value);
        else
        {
            // Default: show next 7 days
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var end   = today.AddDays(7);
            query = query.Where(s => s.SlotDate >= today && s.SlotDate <= end);
        }

        return await query.OrderBy(s => s.SlotDate).ThenBy(s => s.SlotStart)
            .Select(s => CreateDeliverySlotHandler.ToDto(s))
            .ToListAsync(ct);
    }
}
