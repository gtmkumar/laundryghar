using System.Text.Json;
using FluentValidation;
using laundryghar.Orders.Application.Pickup.Dtos;
using laundryghar.SharedDataModel.Logistics;
using MediatR;

namespace laundryghar.Orders.Application.Pickup.Commands;

// ── Admin: create pickup request ──────────────────────────────────────────────

public sealed record CreatePickupRequestAdminCommand(
    CreatePickupRequestRequest Request,
    Guid CustomerId,
    Guid? ActorId
) : IRequest<PickupRequestDto>;

public sealed class CreatePickupRequestAdminHandler
    : IRequestHandler<CreatePickupRequestAdminCommand, PickupRequestDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreatePickupRequestAdminHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<PickupRequestDto> Handle(CreatePickupRequestAdminCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        // Validate the supplied customer belongs to this brand (cross-brand IDOR guard).
        var customerInBrand = await _db.Customers
            .AnyAsync(c => c.Id == cmd.CustomerId && c.BrandId == brandId, ct);
        if (!customerInBrand)
            throw new KeyNotFoundException("Customer not found.");

        return await CreatePickup(_db, brandId, _user.StoreId, cmd.CustomerId, cmd.Request, cmd.ActorId, ct);
    }

    internal static async Task<PickupRequestDto> CreatePickup(
        LaundryGharDbContext db,
        Guid brandId, Guid? storeId, Guid customerId,
        CreatePickupRequestRequest req, Guid? actorId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var count = await db.PickupRequests
            .CountAsync(p => p.BrandId == brandId, ct);
        var requestNumber = $"PKP-{now.Year}-{brandId.ToString()[..4].ToUpper()}-{(count + 1):D6}";

        // Serialise estimated cart items. Null/empty → "[]".
        var cartItems = req.CartItems ?? [];
        var requestedItemsJson = cartItems.Length == 0
            ? "[]"
            : JsonSerializer.Serialize(cartItems, PickupJsonContext.Default.RequestedCartItemDtoArray);

        // Normalise payment preference. "upi" / "card" / anything unknown → "upi-deferred".
        var paymentPref = req.PaymentPreference?.ToLowerInvariant() switch
        {
            "wallet" => "wallet",
            "cod"    => "cod",
            _        => "upi-deferred",
        };

        var entity = new PickupRequest
        {
            Id                 = Guid.NewGuid(),
            RequestNumber      = requestNumber,
            BrandId            = brandId,
            StoreId            = storeId,
            CustomerId         = customerId,
            AddressId          = req.AddressId,
            PickupSlotId       = req.SlotId,
            PickupDate         = req.PickupDate,
            PickupWindowStart  = req.PickupWindowStart,
            PickupWindowEnd    = req.PickupWindowEnd,
            IsExpress          = req.IsExpress,
            EstimatedItems     = req.EstimatedItems ?? (cartItems.Length > 0 ? cartItems.Sum(i => i.Quantity) : null),
            EstimatedAmount    = req.EstimatedAmount ?? (cartItems.Length > 0
                ? cartItems.Sum(i => (i.EstimatedUnitPrice ?? 0m) * i.Quantity)
                : null),
            ServicesRequested  = req.ServicesRequested,
            CustomerNotes      = req.CustomerNotes,
            RequestedItems     = requestedItemsJson,
            PaymentPreference  = paymentPref,
            Status             = "pending",
            Metadata           = "{}",
            CreatedAt          = now,
            UpdatedAt          = now,
            CreatedBy          = actorId,
            UpdatedBy          = actorId
        };

        db.PickupRequests.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    internal static PickupRequestDto ToDto(PickupRequest p)
    {
        // Deserialise the stored JSON back to the DTO list.
        RequestedCartItemDto[] items;
        try
        {
            items = string.IsNullOrWhiteSpace(p.RequestedItems) || p.RequestedItems == "[]"
                ? []
                : JsonSerializer.Deserialize(p.RequestedItems,
                      PickupJsonContext.Default.RequestedCartItemDtoArray) ?? [];
        }
        catch
        {
            items = [];
        }

        return new PickupRequestDto(
            p.Id, p.RequestNumber, p.BrandId, p.StoreId, p.CustomerId, p.AddressId,
            p.PickupSlotId, p.PickupDate, p.PickupWindowStart, p.PickupWindowEnd,
            p.IsExpress, p.EstimatedItems, p.EstimatedAmount, p.Status, p.CreatedAt,
            items,
            p.PaymentPreference);
    }
}

// ── Source-generated JSON context (avoids reflection on hot path) ─────────────

[System.Text.Json.Serialization.JsonSerializable(typeof(RequestedCartItemDto[]))]
[System.Text.Json.Serialization.JsonSerializable(typeof(RequestedCartItemDto))]
internal sealed partial class PickupJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }

// ── Admin: assign pickup to rider ─────────────────────────────────────────────

public sealed record AssignPickupCommand(Guid PickupRequestId, AssignPickupRequest Request, Guid? ActorId)
    : IRequest<DeliveryAssignmentDto?>;

public sealed class AssignPickupHandler : IRequestHandler<AssignPickupCommand, DeliveryAssignmentDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public AssignPickupHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<DeliveryAssignmentDto?> Handle(AssignPickupCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var pr = await _db.PickupRequests
            .FirstOrDefaultAsync(p => p.Id == cmd.PickupRequestId && p.BrandId == brandId, ct);
        if (pr is null) return null;

        // Validate the assigned rider belongs to this brand (cross-brand IDOR guard).
        var riderInBrand = await _db.Riders
            .AnyAsync(r => r.Id == cmd.Request.RiderId && r.BrandId == brandId, ct);
        if (!riderInBrand)
            throw new KeyNotFoundException("Rider not found.");

        var now = DateTimeOffset.UtcNow;

        // ── Store resolution (NOT NULL FK — must not be Guid.Empty) ───────────
        // Priority: pickup request's StoreId → actor's scoped StoreId → rider's PrimaryStoreId
        // → exception (never Guid.Empty).
        var riderPrimaryStoreId = await _db.Riders
            .Where(r => r.Id == cmd.Request.RiderId)
            .Select(r => r.PrimaryStoreId)
            .FirstOrDefaultAsync(ct);

        var resolvedStoreId = pr.StoreId
                           ?? _user.StoreId
                           ?? riderPrimaryStoreId;

        if (!resolvedStoreId.HasValue)
            throw new BusinessRuleException(
                "No store could be resolved for this assignment. " +
                "Ensure the pickup request or rider has a store association.");

        var assignment = new DeliveryAssignment
        {
            Id              = Guid.NewGuid(),
            BrandId         = brandId,
            StoreId         = resolvedStoreId.Value,  // pre-validated; never Guid.Empty
            RiderId         = cmd.Request.RiderId,
            PickupRequestId = cmd.PickupRequestId,
            LegType         = "pickup",
            AssignedAt      = now,
            AssignedBy      = cmd.ActorId,
            AddressSnapshot = "{}",
            OtpVerified     = false,
            Status          = "assigned",
            Metadata        = "{}",
            CreatedAt       = now,
            UpdatedAt       = now,
            CreatedBy       = cmd.ActorId,
            UpdatedBy       = cmd.ActorId
        };

        pr.Status    = "assigned";
        pr.UpdatedAt = now;
        pr.UpdatedBy = cmd.ActorId;

        _db.DeliveryAssignments.Add(assignment);
        await _db.SaveChangesAsync(ct);

        // Bump the rider's current_load now that a new pickup leg is active.
        await RiderLoadHelper.IncrementAsync(_db, cmd.Request.RiderId, ct);

        return new DeliveryAssignmentDto(
            assignment.Id, assignment.BrandId, assignment.StoreId, assignment.RiderId,
            assignment.OrderId, assignment.PickupRequestId, assignment.LegType,
            assignment.AssignedAt, assignment.Status);
    }
}

// ── Customer: schedule pickup with atomic slot booking ───────────────────────

public sealed record CustomerSchedulePickupCommand(
    Guid CustomerId, Guid BrandId,
    CreatePickupRequestRequest Request, Guid? ActorId
) : IRequest<PickupRequestDto>;

public sealed class CustomerSchedulePickupHandler
    : IRequestHandler<CustomerSchedulePickupCommand, PickupRequestDto>
{
    private readonly LaundryGharDbContext _db;

    public CustomerSchedulePickupHandler(LaundryGharDbContext db) => _db = db;

    public async Task<PickupRequestDto> Handle(CustomerSchedulePickupCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        // Atomic slot capacity update — prevents overbooking.
        // UPDATE delivery_slots SET booked_count = booked_count + 1
        // WHERE id = @id AND booked_count < capacity AND is_active = true
        // RETURNING id
        if (req.SlotId.HasValue)
        {
            var updated = await _db.Database.ExecuteSqlAsync(
                $"""
                UPDATE order_lifecycle.delivery_slots
                   SET booked_count = booked_count + 1, updated_at = NOW()
                 WHERE id = {req.SlotId.Value}
                   AND booked_count < capacity
                   AND is_active = true
                   AND brand_id = {cmd.BrandId}
                """, ct);

            if (updated == 0)
                throw new BusinessRuleException(
                    "The selected slot is full or unavailable. Please choose a different time slot.");

            // Record slot booking
            var slot = await _db.DeliverySlots
                .FirstOrDefaultAsync(s => s.Id == req.SlotId.Value, ct);

            if (slot is not null)
            {
                var booking = new DeliverySlotBooking
                {
                    Id          = Guid.NewGuid(),
                    SlotId      = req.SlotId.Value,
                    BrandId     = cmd.BrandId,
                    StoreId     = slot.StoreId,
                    CustomerId  = cmd.CustomerId,
                    BookingType = "pickup",
                    BookedAt    = now,
                    Status      = "active",
                    CreatedAt   = now,
                    CreatedBy   = cmd.CustomerId
                };
                _db.DeliverySlotBookings.Add(booking);
            }
        }

        // Resolve store from slot or brand's default store
        Guid? storeId = null;
        if (req.SlotId.HasValue)
        {
            var slot = await _db.DeliverySlots
                .Where(s => s.Id == req.SlotId.Value)
                .Select(s => s.StoreId)
                .FirstOrDefaultAsync(ct);
            storeId = slot == Guid.Empty ? null : slot;
        }

        var pickup = await CreatePickupRequestAdminHandler.CreatePickup(
            _db, cmd.BrandId, storeId, cmd.CustomerId, req, cmd.CustomerId, ct);

        // Link slot booking to pickup request
        if (req.SlotId.HasValue)
        {
            var booking = await _db.DeliverySlotBookings
                .FirstOrDefaultAsync(b => b.SlotId == req.SlotId.Value
                                       && b.CustomerId == cmd.CustomerId
                                       && b.PickupRequestId == null, ct);
            if (booking is not null)
            {
                booking.PickupRequestId = pickup.Id;
                await _db.SaveChangesAsync(ct);
            }
        }

        return pickup;
    }
}

// ── Admin: reject (cancel) a pending/unassigned pickup request ────────────────

/// <summary>
/// Rejects a pickup request that is still in a rejectable state (pending or unassigned).
/// Uses DB status 'cancelled' (only allowed terminal value in the CHECK constraint)
/// with <c>cancelled_by_type = 'admin'</c> to distinguish from customer self-cancels.
/// The rejection reason is stored in the existing <c>cancellation_reason</c> column.
/// </summary>
public sealed record RejectPickupCommand(Guid PickupRequestId, string Reason, Guid? ActorId)
    : IRequest<PickupRequestDto?>;

public sealed class RejectPickupHandler : IRequestHandler<RejectPickupCommand, PickupRequestDto?>
{
    /// <summary>Statuses from which admin-reject is permitted.</summary>
    private static readonly HashSet<string> RejectableStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "pending" };

    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public RejectPickupHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<PickupRequestDto?> Handle(RejectPickupCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var pr = await _db.PickupRequests
            .FirstOrDefaultAsync(p => p.Id == cmd.PickupRequestId && p.BrandId == brandId, ct);

        if (pr is null) return null;

        // State guard — only pending requests are rejectable.
        if (!RejectableStatuses.Contains(pr.Status))
            throw new BusinessRuleException(
                $"Pickup request cannot be rejected in its current status '{pr.Status}'. " +
                "Only pending requests may be rejected.");

        var now = DateTimeOffset.UtcNow;

        // ── Atomic slot-capacity release ─────────────────────────────────────
        // Mirrors the inverse of CustomerSchedulePickupHandler's booking increment:
        //   UPDATE delivery_slots SET booked_count = booked_count - 1 WHERE id = @slotId
        //     AND booked_count > 0
        // Only runs when a slot was actually linked.
        if (pr.PickupSlotId.HasValue)
        {
            await _db.Database.ExecuteSqlAsync(
                $"""
                UPDATE order_lifecycle.delivery_slots
                   SET booked_count = booked_count - 1, updated_at = NOW()
                 WHERE id = {pr.PickupSlotId.Value}
                   AND booked_count > 0
                   AND brand_id = {brandId}
                """, ct);

            // Mark the corresponding slot booking as cancelled.
            var booking = await _db.DeliverySlotBookings
                .FirstOrDefaultAsync(b => b.PickupRequestId == pr.Id
                                       && b.SlotId         == pr.PickupSlotId.Value
                                       && b.Status         == "active", ct);
            if (booking is not null)
            {
                booking.Status = "cancelled";
            }
        }

        // ── Update pickup request ─────────────────────────────────────────────
        pr.Status             = "cancelled";
        pr.CancellationReason = cmd.Reason;
        pr.CancelledByType    = "admin";
        pr.CancelledById      = cmd.ActorId;
        pr.UpdatedAt          = now;
        pr.UpdatedBy          = cmd.ActorId;

        // ── Kernel outbox event (pickup.rejected) ─────────────────────────────
        // Orderless event — aggregate is the pickup request itself.
        // Worker NotificationMappingService maps this to PICKUP_REJECTED_{WHATSAPP,SMS,PUSH}.
        var outbox = new OutboxEvent
        {
            Id            = Guid.NewGuid(),
            BrandId       = brandId,
            AggregateType = "pickup_request",
            AggregateId   = pr.Id,
            EventType     = "pickup.rejected",
            EventVersion  = 1,
            Payload       = System.Text.Json.JsonSerializer.Serialize(new
            {
                pickupRequestId = pr.Id,
                requestNumber   = pr.RequestNumber,
                customerId      = pr.CustomerId,
                reason          = cmd.Reason,
                rejectedAt      = now
            }),
            Metadata   = "{}",
            OccurredAt = now,
            Status     = "pending",
            CreatedAt  = now,
            CreatedBy  = cmd.ActorId
        };

        _db.OutboxEvents.Add(outbox);
        await _db.SaveChangesAsync(ct);

        return CreatePickupRequestAdminHandler.ToDto(pr);
    }
}

public sealed class RejectPickupValidator : AbstractValidator<RejectPickupCommand>
{
    public RejectPickupValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A rejection reason is required.")
            .MaximumLength(300).WithMessage("Rejection reason must not exceed 300 characters.");
    }
}

public sealed class CreatePickupRequestAdminValidator : AbstractValidator<CreatePickupRequestAdminCommand>
{
    public CreatePickupRequestAdminValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Request.PickupDate).NotEmpty();

        RuleFor(x => x.Request.CartItems)
            .Must(items => items is null || items.Length <= 50)
            .WithMessage("A pickup request may include at most 50 estimated items.");

        When(x => x.Request.CartItems is { Length: > 0 }, () =>
        {
            RuleForEach(x => x.Request.CartItems)
                .ChildRules(item =>
                {
                    item.RuleFor(i => i.DisplayLabel)
                        .NotEmpty().WithMessage("Each item must have a display label.")
                        .MaximumLength(120).WithMessage("Item display label must not exceed 120 characters.");
                    item.RuleFor(i => i.Quantity)
                        .GreaterThanOrEqualTo(1).WithMessage("Item quantity must be at least 1.");
                    item.RuleFor(i => i.EstimatedUnitPrice)
                        .GreaterThanOrEqualTo(0m).When(i => i.EstimatedUnitPrice.HasValue)
                        .WithMessage("Estimated unit price must be >= 0.");
                });
        });
    }
}

public sealed class AssignPickupValidator : AbstractValidator<AssignPickupCommand>
{
    public AssignPickupValidator()
    {
        RuleFor(x => x.PickupRequestId).NotEmpty();
        RuleFor(x => x.Request.RiderId).NotEmpty();
    }
}

public sealed class CustomerSchedulePickupValidator : AbstractValidator<CustomerSchedulePickupCommand>
{
    public CustomerSchedulePickupValidator()
    {
        RuleFor(x => x.Request.AddressId).NotEmpty();
        RuleFor(x => x.Request.PickupDate).NotEmpty();

        // CartItems validation — items are optional but bounded when provided.
        RuleFor(x => x.Request.CartItems)
            .Must(items => items is null || items.Length <= 50)
            .WithMessage("A pickup request may include at most 50 estimated items.");

        When(x => x.Request.CartItems is { Length: > 0 }, () =>
        {
            RuleForEach(x => x.Request.CartItems)
                .ChildRules(item =>
                {
                    item.RuleFor(i => i.DisplayLabel)
                        .NotEmpty().WithMessage("Each item must have a display label.")
                        .MaximumLength(120).WithMessage("Item display label must not exceed 120 characters.");

                    item.RuleFor(i => i.Quantity)
                        .GreaterThanOrEqualTo(1).WithMessage("Item quantity must be at least 1.");

                    item.RuleFor(i => i.EstimatedUnitPrice)
                        .GreaterThanOrEqualTo(0m).When(i => i.EstimatedUnitPrice.HasValue)
                        .WithMessage("Estimated unit price must be >= 0.");
                });
        });
    }
}
