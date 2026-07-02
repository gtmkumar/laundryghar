using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Settings.Commands.UpsertSetting;
using operations.Application.Settings.Dtos;
using operations.Tests.Catalog.Import;
using Xunit;
using ValidationException = laundryghar.Utilities.Exceptions.ValidationException;

namespace operations.Tests.Settings;

/// <summary>
/// Write-path coverage for the upsert handler: the brand's clamp band is enforced on a
/// franchise/store write, only HQ (brand scope) may set a validation schema, and a null value
/// clears the row. Runs against an in-memory operations context with a scriptable current user.
/// </summary>
public class SettingsWritePathTests
{
    private static void SeedFranchise(LaundryGharDbContext raw, Guid franchiseId, Guid brandId)
        => raw.Franchises.Add(new Franchise
        {
            Id = franchiseId, BrandId = brandId, Code = "F1", LegalName = "F1",
            ContactPhone = "+910000000000", BillingAddress = "{}", Config = "{}", Metadata = "{}",
            OnboardingStatus = "active", Status = "active",
        });

    [Fact]
    public async Task Franchise_write_outside_brand_clamp_is_rejected_and_within_band_is_accepted()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid(); var franchise = Guid.NewGuid();
        SeedFranchise(raw, franchise, brand);
        await raw.SaveChangesAsync();

        var user = new ImportTestSupport.FakeCurrentUser(brand);
        var handler = new UpsertSettingHandler(db, user);

        // HQ sets the brand value and a 10..20 clamp band.
        await handler.HandleAsync(new UpsertSettingCommand(new UpsertSettingRequest(
            "orders", "tax_rate_percent", "brand", null, null, "18", "decimal", "{\"min\":10,\"max\":20}"),
            user.UserId), default);

        // A franchise override outside the band is rejected.
        await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(new UpsertSettingCommand(
            new UpsertSettingRequest("orders", "tax_rate_percent", "franchise", franchise, null, "25", "decimal", null),
            user.UserId), default));

        // A franchise override inside the band is accepted.
        var ok = await handler.HandleAsync(new UpsertSettingCommand(new UpsertSettingRequest(
            "orders", "tax_rate_percent", "franchise", franchise, null, "15", "decimal", null),
            user.UserId), default);
        Assert.NotNull(ok);
        Assert.Equal("franchise", ok!.ScopeType);
        Assert.Equal(15m, decimal.Parse(ok.Value));
    }

    [Fact]
    public async Task Only_brand_scope_may_set_a_validation_schema()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid(); var franchise = Guid.NewGuid();
        SeedFranchise(raw, franchise, brand);
        await raw.SaveChangesAsync();

        var user = new ImportTestSupport.FakeCurrentUser(brand);
        var handler = new UpsertSettingHandler(db, user);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.HandleAsync(new UpsertSettingCommand(
            new UpsertSettingRequest("orders", "tax_rate_percent", "franchise", franchise, null, "15", "decimal",
                "{\"min\":0,\"max\":100}"),
            user.UserId), default));
    }

    [Fact]
    public async Task Null_value_clears_the_scope_row()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();

        var user = new ImportTestSupport.FakeCurrentUser(brand);
        var handler = new UpsertSettingHandler(db, user);

        var created = await handler.HandleAsync(new UpsertSettingCommand(new UpsertSettingRequest(
            "orders", "min_order_value", "brand", null, null, "199", "decimal", null), user.UserId), default);
        Assert.NotNull(created);

        var cleared = await handler.HandleAsync(new UpsertSettingCommand(new UpsertSettingRequest(
            "orders", "min_order_value", "brand", null, null, null, "decimal", null), user.UserId), default);
        Assert.Null(cleared);

        var remaining = await db.SystemSettings.CountAsync(
            s => s.BrandId == brand && s.Category == "orders" && s.SettingKey == "min_order_value");
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task Out_of_scope_franchise_write_is_forbidden()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid(); var franchise = Guid.NewGuid();
        SeedFranchise(raw, franchise, brand);
        await raw.SaveChangesAsync();

        // withinScope:false simulates a franchise/store-scoped user targeting a sibling scope.
        var user = new ImportTestSupport.FakeCurrentUser(brand, withinScope: false);
        var handler = new UpsertSettingHandler(db, user);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.HandleAsync(new UpsertSettingCommand(
            new UpsertSettingRequest("orders", "tax_rate_percent", "franchise", franchise, null, "15", "decimal", null),
            user.UserId), default));
    }
}
