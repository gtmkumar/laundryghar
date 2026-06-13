using laundryghar.Orders.Infrastructure.Auth;
using laundryghar.Orders.Infrastructure.Services;
using System.Text.Json;
using FluentValidation;
using laundryghar.Orders.Application.Pickup.Dtos;
using laundryghar.SharedDataModel.Logistics;
using MediatR;
using Npgsql;

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
        _db = db;
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

        return await CreatePickup(_db, brandId, _user.StoreId, cmd.CustomerId, cmd.Request, cmd.ActorId, ct,
            source: "pos"); // admin-created requests are tagged as 'pos' origin
    }

    internal static async Task<PickupRequestDto> CreatePickup(
        LaundryGharDbContext db,
        Guid brandId, Guid? storeId, Guid customerId,
        CreatePickupRequestRequest req, Guid? actorId,
        CancellationToken ct,
        string? idempotencyKey = null,
        string source = "app")
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
            "cod" => "cod",
            _ => "upi-deferred",
        };

        // Normalise idempotency key — trim whitespace; treat blank as null.
        var normKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();

        var entity = new PickupRequest
        {
            Id = Guid.NewGuid(),
            RequestNumber = requestNumber,
            BrandId = brandId,
            StoreId = storeId,
            CustomerId = customerId,
            AddressId = req.AddressId,
            PickupSlotId = req.SlotId,
            PickupDate = req.PickupDate,
            PickupWindowStart = req.PickupWindowStart,
            PickupWindowEnd = req.PickupWindowEnd,
            IsExpress = req.IsExpress,
            EstimatedItems = req.EstimatedItems ?? (cartItems.Length > 0 ? cartItems.Sum(i => i.Quantity) : null),
            EstimatedAmount = req.EstimatedAmount ?? (cartItems.Length > 0
                ? cartItems.Sum(i => (i.EstimatedUnitPrice ?? 0m) * i.Quantity)
                : null),
            // JSON deserialization leaves the array null when the field is omitted
            // (the DTO type is non-nullable but STJ doesn't enforce it), and the
            // entity's primitive collection is required — default to empty.
            ServicesRequested = req.ServicesRequested ?? [],
            CustomerNotes = req.CustomerNotes,
            RequestedItems = requestedItemsJson,
            PaymentPreference = paymentPref,
            IdempotencyKey = normKey,
            Source = source,
            // Store normalised coupon code for admin conversion path.
            CouponCode = string.IsNullOrWhiteSpace(req.CouponCode) ? null : req.CouponCode.Trim().ToUpperInvariant(),
            Status = "pending",
            Metadata = "{}",
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = actorId,
            UpdatedBy = actorId
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
            p.PaymentPreference,
            Source: p.Source,
            IdempotencyKey: p.IdempotencyKey,
            CouponCode: p.CouponCode);
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
        _db = db;
        _user = user;
    }

    /// <summary>
    /// COD-CASH (pickup leg) — the amount of cash the rider is expected to collect at a
    /// pickup, mirroring the delivery-leg COD model (DeliveryAssignment.CodAmount feeds the
    /// same rider-cash settlement pipeline; see SettleRiderCodHandler / GetCodOutstanding).
    ///
    /// A pickup has COD due only when the customer chose to pay cash on collection
    /// (PaymentPreference == "cod") AND there is a positive estimated amount to collect.
    /// Wallet / upi-deferred preferences are settled when the order is confirmed after
    /// weighing, so the rider collects nothing — returns null in that case.
    /// Pure + static so it is unit-testable without a DbContext.
    /// </summary>
    internal static decimal? ResolvePickupCodAmount(string? paymentPreference, decimal? estimatedAmount)
    {
        var isCod = string.Equals(paymentPreference?.Trim(), "cod", StringComparison.OrdinalIgnoreCase);
        if (!isCod) return null;
        var amount = estimatedAmount ?? 0m;
        return amount > 0m ? amount : null;
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

        // COD-CASH (pickup leg): seed the cash the rider is expected to collect from the
        // pickup request, so the rider-cash settlement pipeline can later see it. Stamped
        // here at assign-time; the actual collected_at is set when the rider collects.
        var pickupCod = ResolvePickupCodAmount(pr.PaymentPreference, pr.EstimatedAmount);

        var assignment = new DeliveryAssignment
        {
            Id = Guid.NewGuid(),
            BrandId = brandId,
            StoreId = resolvedStoreId.Value,  // pre-validated; never Guid.Empty
            RiderId = cmd.Request.RiderId,
            PickupRequestId = cmd.PickupRequestId,
            LegType = "pickup",
            AssignedAt = now,
            AssignedBy = cmd.ActorId,
            AddressSnapshot = "{}",
            CodAmount = pickupCod,
            OtpVerified = false,
            Status = "assigned",
            Metadata = "{}",
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = cmd.ActorId,
            UpdatedBy = cmd.ActorId
        };

        pr.Status = "assigned";
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
    CreatePickupRequestRequest Request, Guid? ActorId,
    /// <summary>
    /// Resolved idempotency key. Prefer the Idempotency-Key HTTP header value
    /// (passed by the endpoint layer); fall back to Request.IdempotencyKey.
    /// Null means no idempotency guard is applied.
    /// </summary>
    string? ResolvedIdempotencyKey = null,
    /// <summary>
    /// Resolved booking source. Prefer the X-Channel HTTP header; fall back to
    /// Request.Channel; default to "app". Validated against the DB CHECK list.
    /// </summary>
    string ResolvedSource = "app"
) : IRequest<CustomerSchedulePickupResult>;

/// <summary>
/// Result of <see cref="CustomerSchedulePickupCommand"/>.
/// <see cref="AlreadyExisted"/> is true when idempotency short-circuited and the
/// existing request was returned without creating a new one.
/// </summary>
public sealed record CustomerSchedulePickupResult(
    PickupRequestDto Dto,
    bool AlreadyExisted
);

public sealed class CustomerSchedulePickupHandler
    : IRequestHandler<CustomerSchedulePickupCommand, CustomerSchedulePickupResult>
{
    /// <summary>Allowed source values — mirrors the DB CHECK constraint.</summary>
    private static readonly HashSet<string> AllowedSources =
        new(StringComparer.OrdinalIgnoreCase) { "app", "web", "mcp", "whatsapp", "pos", "call" };

    private readonly LaundryGharDbContext _db;
    private readonly ILogger<CustomerSchedulePickupHandler> _logger;

    public CustomerSchedulePickupHandler(
        LaundryGharDbContext db,
        ILogger<CustomerSchedulePickupHandler> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <summary>PostgreSQL unique-violation SQLSTATE code.</summary>
    private const string PgUniqueViolation = "23505";

    /// <summary>
    /// Name of the partial unique index that enforces per-customer idempotency key
    /// uniqueness. Used to distinguish an idempotency collision from any other
    /// unique-constraint violation on pickup_requests.
    /// </summary>
    private const string IdempotencyIndexName = "pickup_requests_customer_idempotency_key";

    public async Task<CustomerSchedulePickupResult> Handle(CustomerSchedulePickupCommand cmd, CancellationToken ct)
    {
        var req    = cmd.Request;
        var source = AllowedSources.Contains(cmd.ResolvedSource) ? cmd.ResolvedSource.ToLowerInvariant() : "app";
        var normKey = string.IsNullOrWhiteSpace(cmd.ResolvedIdempotencyKey)
            ? null
            : cmd.ResolvedIdempotencyKey.Trim();

        // ── Fast-path idempotency check (optimistic — avoids transaction overhead for retries) ──
        // A matching row from a prior successful insert returns immediately.
        // The race case (two concurrent requests, both miss this check) is handled
        // below by catching the unique-index violation inside the transaction.
        if (normKey is not null)
        {
            var existing = await _db.PickupRequests
                .FirstOrDefaultAsync(p => p.CustomerId     == cmd.CustomerId
                                       && p.IdempotencyKey == normKey, ct);
            if (existing is not null)
                return new CustomerSchedulePickupResult(
                    CreatePickupRequestAdminHandler.ToDto(existing), AlreadyExisted: true);
        }

        // ── DEFECT 2: validate cart-item catalog FKs (brand-scoped) ───────────
        // The mobile app previously passed a price-list ROW id as itemId (with a
        // null serviceId) and the request was accepted, writing a bogus reference.
        // Reject any cartItems[i].itemId that is not a live customer_catalog item
        // for this brand, and any non-null serviceId that doesn't resolve. The
        // error dictionary keys carry the offending index so the client can map it.
        await ValidateCartItemsAsync(req.CartItems, cmd.BrandId, ct);

        // ── Resolve slot store before opening the transaction ─────────────────
        // ExecuteSqlAsync inside the transaction runs on the same connection; we
        // can do read-only look-ups outside to keep the critical section tight.
        Guid? storeId = null;
        if (req.SlotId.HasValue)
        {
            var slotStore = await _db.DeliverySlots
                .Where(s => s.Id == req.SlotId.Value)
                .Select(s => s.StoreId)
                .FirstOrDefaultAsync(ct);
            storeId = slotStore == Guid.Empty ? null : slotStore;
        }

        // ── Single transaction: slot increment + pickup insert + slot-booking link ──
        //
        // Ordering guarantee: the slot booked_count UPDATE and the pickup_requests
        // INSERT both happen inside the same transaction. If the INSERT loses the
        // unique-index race (23505 on idempotency key), the entire transaction —
        // including the booked_count increment — rolls back atomically. There is no
        // phantom capacity leak regardless of slot usage.
        //
        // CreateExecutionStrategy wrapper is required because AddSharedDataModel
        // registers NpgsqlRetryingExecutionStrategy, which rejects direct
        // BeginTransactionAsync calls (see project-retry-strategy memory).
        PickupRequestDto pickupDto;
        try
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            pickupDto = await strategy.ExecuteAsync(async () =>
            {
                var now = DateTimeOffset.UtcNow;
                await using var tx = await _db.Database.BeginTransactionAsync(ct);

                // Atomic slot capacity increment (prevents overbooking).
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

                    // Record slot booking entity (persisted in SaveChangesAsync below).
                    var slot = await _db.DeliverySlots
                        .FirstOrDefaultAsync(s => s.Id == req.SlotId.Value, ct);

                    if (slot is not null)
                    {
                        _db.DeliverySlotBookings.Add(new DeliverySlotBooking
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
                        });
                    }
                }

                // Build and add the pickup request entity.
                // CreatePickup calls SaveChangesAsync internally — slot booking and
                // pickup row are flushed together before CommitAsync.
                var dto = await CreatePickupRequestAdminHandler.CreatePickup(
                    db: _db, brandId: cmd.BrandId, storeId: storeId, customerId: cmd.CustomerId,
                    req: req, actorId: cmd.CustomerId, ct: ct,
                    idempotencyKey: normKey,
                    source: source);

                // Link slot booking → pickup request (second SaveChanges, same tx).
                if (req.SlotId.HasValue)
                {
                    var booking = await _db.DeliverySlotBookings
                        .FirstOrDefaultAsync(b => b.SlotId          == req.SlotId.Value
                                               && b.CustomerId      == cmd.CustomerId
                                               && b.PickupRequestId == null, ct);
                    if (booking is not null)
                    {
                        booking.PickupRequestId = dto.Id;
                        await _db.SaveChangesAsync(ct);
                    }
                }

                await tx.CommitAsync(ct);
                return dto;
            });
        }
        catch (DbUpdateException dbEx)
            when (IsIdempotencyKeyViolation(dbEx))
        {
            // The concurrent winner already committed a row with the same
            // (customer_id, idempotency_key). The transaction (including any slot
            // increment) was rolled back by Postgres before we even see this
            // exception — no capacity leak is possible.
            // Re-query and return the winner's row on the 200 (AlreadyExisted) path.
            _logger.LogInformation(
                "Idempotency collision for customer {CustomerId} key {Key} — returning existing row",
                cmd.CustomerId, normKey);

            var winner = await _db.PickupRequests
                .FirstOrDefaultAsync(p => p.CustomerId     == cmd.CustomerId
                                       && p.IdempotencyKey == normKey, ct);

            if (winner is null)
            {
                // Extremely unlikely (winner deleted between our catch and re-query).
                // Surface as a retriable error rather than hiding it.
                _logger.LogError(dbEx,
                    "Idempotency winner row not found after collision for customer {CustomerId} key {Key}",
                    cmd.CustomerId, normKey);
                throw;
            }

            return new CustomerSchedulePickupResult(
                CreatePickupRequestAdminHandler.ToDto(winner), AlreadyExisted: true);
        }

        return new CustomerSchedulePickupResult(pickupDto, AlreadyExisted: false);
    }

    /// <summary>
    /// Returns true when <paramref name="ex"/> is a PostgreSQL unique-constraint
    /// violation (SQLSTATE 23505) raised by the idempotency key partial index.
    /// Inspects the inner <see cref="PostgresException"/> via the standard EF Core
    /// <see cref="DbUpdateException.InnerException"/> chain.
    /// </summary>
    /// <remarks>Internal for unit-testing; production callers use the <c>when</c> filter on the catch block.</remarks>
    internal static bool IsIdempotencyKeyViolation(DbUpdateException ex)
    {
        if (ex.InnerException is not PostgresException pg) return false;
        if (pg.SqlState != PgUniqueViolation) return false;
        // ConstraintName is populated by Npgsql on 23505; fall back to message scan
        // in case the driver version omits it.
        return pg.ConstraintName == IdempotencyIndexName
            || (pg.ConstraintName is null && pg.Message.Contains(IdempotencyIndexName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// DEFECT 2 — validates that every cart item references a live, brand-scoped
    /// catalog item (and a live service when a serviceId is supplied). Throws a
    /// <see cref="ValidationException"/> (→ HTTP 422) whose error dictionary keys
    /// identify the offending cart index, e.g. <c>cartItems[0].itemId</c>.
    /// A null itemId is rejected outright — an item reference is required to book.
    /// Internal so unit tests can exercise it directly.
    /// </summary>
    internal static async Task ValidateCartItemsAsync(
        RequestedCartItemDto[]? cartItems, Guid brandId, CancellationToken ct,
        LaundryGharDbContext? db = null)
    {
        // Allow the instance handler to pass its own context; static test callers pass db.
        ArgumentNullException.ThrowIfNull(db);

        if (cartItems is null || cartItems.Length == 0) return;

        // Collect the distinct ids to validate in two set-based queries (no N+1).
        var itemIds = cartItems
            .Where(c => c.ItemId.HasValue)
            .Select(c => c.ItemId!.Value)
            .Distinct()
            .ToList();

        var serviceIds = cartItems
            .Where(c => c.ServiceId.HasValue)
            .Select(c => c.ServiceId!.Value)
            .Distinct()
            .ToList();

        var validItemIds = itemIds.Count == 0
            ? new HashSet<Guid>()
            : (await db.Items
                .Where(i => itemIds.Contains(i.Id)
                         && i.BrandId == brandId
                         && i.DeletedAt == null)
                .Select(i => i.Id)
                .ToListAsync(ct))
                .ToHashSet();

        var validServiceIds = serviceIds.Count == 0
            ? new HashSet<Guid>()
            : (await db.Services
                .Where(s => serviceIds.Contains(s.Id)
                         && s.BrandId == brandId
                         && s.DeletedAt == null)
                .Select(s => s.Id)
                .ToListAsync(ct))
                .ToHashSet();

        var errors = BuildCartItemErrors(cartItems, validItemIds, validServiceIds);

        if (errors.Count > 0)
            throw new laundryghar.Utilities.Exceptions.ValidationException(errors);
    }

    /// <summary>
    /// Pure per-index validation: given the set of catalog item/service ids that DO
    /// exist for the brand, returns an error dictionary keyed by the offending cart
    /// index (empty when all items are valid). Split out from the DB-touching method
    /// so it is unit-testable without a DbContext.
    /// </summary>
    internal static Dictionary<string, string[]> BuildCartItemErrors(
        RequestedCartItemDto[] cartItems,
        ISet<Guid> validItemIds,
        ISet<Guid> validServiceIds)
    {
        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < cartItems.Length; i++)
        {
            var c = cartItems[i];

            if (!c.ItemId.HasValue)
            {
                errors[$"cartItems[{i}].itemId"] =
                    ["An itemId is required for each cart item."];
            }
            else if (!validItemIds.Contains(c.ItemId.Value))
            {
                errors[$"cartItems[{i}].itemId"] =
                    [$"itemId '{c.ItemId.Value}' is not a valid catalog item for this brand."];
            }

            if (c.ServiceId.HasValue && !validServiceIds.Contains(c.ServiceId.Value))
            {
                errors[$"cartItems[{i}].serviceId"] =
                    [$"serviceId '{c.ServiceId.Value}' is not a valid service for this brand."];
            }
        }
        return errors;
    }

    /// <summary>Instance overload used by the handler (binds the injected DbContext).</summary>
    private Task ValidateCartItemsAsync(RequestedCartItemDto[]? cartItems, Guid brandId, CancellationToken ct)
        => ValidateCartItemsAsync(cartItems, brandId, ct, _db);
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
        _db = db;
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
                                       && b.SlotId == pr.PickupSlotId.Value
                                       && b.Status == "active", ct);
            if (booking is not null)
            {
                booking.Status = "cancelled";
            }
        }

        // ── Update pickup request ─────────────────────────────────────────────
        pr.Status = "cancelled";
        pr.CancellationReason = cmd.Reason;
        pr.CancelledByType = "admin";
        pr.CancelledById = cmd.ActorId;
        pr.UpdatedAt = now;
        pr.UpdatedBy = cmd.ActorId;

        // ── Kernel outbox event (pickup.rejected) ─────────────────────────────
        // Orderless event — aggregate is the pickup request itself.
        // Worker NotificationMappingService maps this to PICKUP_REJECTED_{WHATSAPP,SMS,PUSH}.
        var outbox = new OutboxEvent
        {
            Id = Guid.NewGuid(),
            BrandId = brandId,
            AggregateType = "pickup_request",
            AggregateId = pr.Id,
            EventType = "pickup.rejected",
            EventVersion = 1,
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                pickupRequestId = pr.Id,
                requestNumber = pr.RequestNumber,
                customerId = pr.CustomerId,
                reason = cmd.Reason,
                rejectedAt = now
            }),
            Metadata = "{}",
            OccurredAt = now,
            Status = "pending",
            CreatedAt = now,
            CreatedBy = cmd.ActorId
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
    private static readonly HashSet<string> ValidSources =
        new(StringComparer.OrdinalIgnoreCase) { "app", "web", "mcp", "whatsapp", "pos", "call" };

    public CustomerSchedulePickupValidator()
    {
        RuleFor(x => x.Request.AddressId).NotEmpty();
        RuleFor(x => x.Request.PickupDate).NotEmpty();

        RuleFor(x => x.ResolvedIdempotencyKey)
            .MaximumLength(150).When(x => x.ResolvedIdempotencyKey is not null)
            .WithMessage("Idempotency key must not exceed 150 characters.");

        RuleFor(x => x.ResolvedSource)
            .Must(s => ValidSources.Contains(s))
            .WithMessage("Channel must be one of: app, web, mcp, whatsapp, pos, call.");

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
