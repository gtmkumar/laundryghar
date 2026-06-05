using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace laundryghar.Orders.Infrastructure.Seeders;

/// <summary>
/// Idempotent orders seeder — Development only, prod-guarded.
/// Seeds:
///   1. One franchise + one store under LG-MAIN brand (required by orders.franchise_id FK).
///   2. Delivery slots (pickup + delivery) for the seeded store over the next 7 days.
/// Check-before-insert on natural keys. Resolves brand at runtime; skips if absent.
/// </summary>
public sealed class OrdersSeeder
{
    private readonly LaundryGharDbContext _db;
    private readonly IHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<OrdersSeeder> _logger;

    public OrdersSeeder(
        LaundryGharDbContext db,
        IHostEnvironment env,
        IConfiguration config,
        ILogger<OrdersSeeder> logger)
    {
        _db     = db;
        _env    = env;
        _config = config;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!_env.IsDevelopment())
            throw new InvalidOperationException(
                "OrdersSeeder may only run in Development.");

        _logger.LogInformation("Running orders seeder...");

        var brand = await _db.Brands.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Code == "LG-MAIN", ct);
        if (brand is null)
        {
            _logger.LogWarning("Brand LG-MAIN not found. Run Identity seeder first. Skipping.");
            return;
        }

        var now = DateTimeOffset.UtcNow;

        // ── 1. Franchise ──────────────────────────────────────────────────────
        var franchise = await _db.Franchises.IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.BrandId == brand.Id && f.Code == "LGF-MAIN", ct);
        if (franchise is null)
        {
            franchise = new Franchise
            {
                Id                = Guid.NewGuid(),
                BrandId           = brand.Id,
                Code              = "LGF-MAIN",
                LegalName         = "Laundry Ghar Franchise One",
                DisplayName       = "LG Main Franchise",
                ContactPhone      = "+919000000001",
                BillingAddress    = "{\"city\":\"Mumbai\"}",
                RoyaltyPercent    = 5m,
                MarketingFeePercent = 2m,
                OnboardingStatus  = "active",
                Config            = "{}",
                Metadata          = "{}",
                Status            = "active",
                CreatedAt         = now,
                UpdatedAt         = now,
                Version           = 1
            };
            _db.Franchises.Add(franchise);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded franchise {Id}.", franchise.Id);
        }

        // ── 2. Store ──────────────────────────────────────────────────────────
        var store = await _db.Stores.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.BrandId == brand.Id && s.Code == "LGS-MUM-001", ct);
        if (store is null)
        {
            store = new Store
            {
                Id                   = Guid.NewGuid(),
                BrandId              = brand.Id,
                FranchiseId          = franchise.Id,
                Code                 = "LGS-MUM-001",
                Name                 = "Laundry Ghar Mumbai Central",
                StoreType            = "walkin",
                AddressLine1         = "123 Linking Road",
                City                 = "Mumbai",
                State                = "Maharashtra",
                Pincode              = "400050",
                CountryCode          = "IN",
                ServiceRadiusKm      = 5m,
                Timezone             = "Asia/Kolkata",
                CurrencyCode         = "INR",
                DailyPickupCapacity  = 50,
                DailyDeliveryCapacity = 50,
                SlotDurationMinutes  = 60,
                AcceptsExpress       = true,
                AcceptsCod           = true,
                AcceptsWalkin        = true,
                RatingCount          = 0,
                Config               = "{}",
                Status               = "active",
                CreatedAt            = now,
                UpdatedAt            = now,
                Version              = 1
            };
            _db.Stores.Add(store);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded store {Id}.", store.Id);
        }

        // ── 3. Delivery slots (next 7 days, pickup + delivery) ────────────────
        var existingSlotsList = await _db.DeliverySlots
            .Where(s => s.BrandId == brand.Id && s.StoreId == store.Id)
            .Select(s => new { s.SlotDate, s.SlotType, s.SlotStart })
            .ToListAsync(ct);
        var existingSlots = existingSlotsList
            .Select(s => $"{s.SlotDate}|{s.SlotType}|{s.SlotStart}")
            .ToHashSet();

        int slotsAdded = 0;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        for (int dayOffset = 1; dayOffset <= 7; dayOffset++)
        {
            var slotDate = today.AddDays(dayOffset);

            foreach (var (slotType, start, end) in new[]
            {
                ("pickup",   new TimeOnly(9, 0),  new TimeOnly(11, 0)),
                ("pickup",   new TimeOnly(11, 0), new TimeOnly(13, 0)),
                ("delivery", new TimeOnly(17, 0), new TimeOnly(19, 0)),
                ("delivery", new TimeOnly(19, 0), new TimeOnly(21, 0)),
            })
            {
                var key = $"{slotDate}|{slotType}|{start}";
                if (existingSlots.Contains(key)) continue;

                _db.DeliverySlots.Add(new DeliverySlot
                {
                    Id          = Guid.NewGuid(),
                    BrandId     = brand.Id,
                    StoreId     = store.Id,
                    SlotDate    = slotDate,
                    SlotStart   = start,
                    SlotEnd     = end,
                    SlotType    = slotType,
                    Capacity    = 20,
                    BookedCount = 0,
                    IsExpress   = false,
                    IsActive    = true,
                    Status      = "active",
                    CreatedAt   = now,
                    UpdatedAt   = now
                });
                existingSlots.Add(key);
                slotsAdded++;
            }
        }

        if (slotsAdded > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded {Count} delivery slots.", slotsAdded);
        }

        _logger.LogInformation("Orders seeding complete.");
    }
}
