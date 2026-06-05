namespace laundryghar.Finance.Infrastructure.Seeders;

/// <summary>
/// Idempotent finance seeder. Seeds expense categories under the LG-MAIN brand.
/// ONLY runs in Development. Prod-guarded: throws if not Development environment.
/// </summary>
public sealed class FinanceSeeder
{
    private readonly LaundryGharDbContext            _db;
    private readonly Microsoft.Extensions.Hosting.IHostEnvironment _env;
    private readonly ILogger<FinanceSeeder>          _logger;

    public FinanceSeeder(
        LaundryGharDbContext db,
        Microsoft.Extensions.Hosting.IHostEnvironment env,
        ILogger<FinanceSeeder> logger)
    {
        _db     = db;
        _env    = env;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!_env.IsDevelopment())
        {
            throw new InvalidOperationException(
                "FinanceSeeder may only run in Development. " +
                "Use a dedicated bootstrap tool for production environments.");
        }

        _logger.LogInformation("Running Finance seeders...");

        await SeedExpenseCategoriesAsync(ct);

        _logger.LogInformation("Finance seeding complete.");
    }

    // ── Expense categories ─────────────────────────────────────────────────

    private static readonly (string Code, string Name, string Description, bool TaxDeductible, bool RequiresApproval, short Order)[] CategoryDefs =
    [
        ("RENT",        "Rent",         "Store or warehouse rent payments",              true,  true,  1),
        ("UTILITY",     "Utility",      "Electricity, water, gas, internet bills",        true,  false, 2),
        ("SALARY",      "Salary",       "Staff salary and wages",                         true,  true,  3),
        ("SUPPLIES",    "Supplies",     "Cleaning supplies, packaging, consumables",       true,  false, 4),
        ("MAINTENANCE", "Maintenance",  "Equipment maintenance and repair",                true,  false, 5),
    ];

    private async Task SeedExpenseCategoriesAsync(CancellationToken ct)
    {
        // Resolve LG-MAIN brand — must already exist (seeded by IdentitySeeder)
        var brand = await _db.Brands.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Code == "LG-MAIN", ct);

        if (brand is null)
        {
            _logger.LogWarning("LG-MAIN brand not found; skipping expense category seeding. Run Identity seeder first.");
            return;
        }

        var existing = await _db.ExpenseCategories
            .Where(c => c.BrandId == brand.Id)
            .Select(c => c.Code)
            .ToHashSetAsync(ct);

        var now   = DateTimeOffset.UtcNow;
        int added = 0;

        foreach (var (code, name, description, taxDeductible, requiresApproval, order) in CategoryDefs)
        {
            if (existing.Contains(code)) continue;

            _db.ExpenseCategories.Add(new ExpenseCategory
            {
                Id               = Guid.NewGuid(),
                BrandId          = brand.Id,
                ParentId         = null,
                Code             = code,
                Name             = name,
                NameLocalized    = "{}",
                Description      = description,
                IsTaxDeductible  = taxDeductible,
                RequiresApproval = requiresApproval,
                DisplayOrder     = order,
                IsActive         = true,
                Status           = "active",   // CHECK: active|inactive|archived
                CreatedAt        = now,
                UpdatedAt        = now,
                CreatedBy        = null,
                UpdatedBy        = null
            });
            added++;
        }

        if (added > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded {Count} expense categories.", added);
        }
        else
        {
            _logger.LogInformation("Expense categories already seeded; nothing to do.");
        }
    }
}
