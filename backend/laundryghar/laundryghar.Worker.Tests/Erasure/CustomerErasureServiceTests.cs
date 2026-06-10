using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.Worker.Services;

// CustomerAnonymizer is in the laundryghar.Worker assembly (referenced via ProjectReference).

namespace laundryghar.Worker.Tests.Erasure;

/// <summary>
/// Unit tests for the pure <see cref="CustomerAnonymizer.Anonymize"/> logic.
/// No database, no DI — every test is a plain in-memory value assertion.
/// </summary>
public sealed class CustomerAnonymizationTests
{
    private static Customer BuildCustomer(Guid? id = null) => new()
    {
        Id             = id ?? Guid.NewGuid(),
        BrandId        = Guid.NewGuid(),
        CustomerCode   = "C-001",
        PhoneE164      = "+919876543210",
        Email          = "alice@example.com",
        FirstName      = "Alice",
        LastName       = "Smith",
        DisplayName    = "Alice S",
        Gender         = "female",
        DateOfBirth    = new DateOnly(1990, 1, 1),
        AvatarUrl      = "https://cdn.example.com/alice.jpg",
        Locale         = "en-IN",
        Timezone       = "Asia/Kolkata",
        MarketingOptIn = true,
        SmsOptIn       = true,
        WhatsappOptIn  = true,
        EmailOptIn     = true,
        PushOptIn      = true,
        Status         = "active",
        Metadata       = "{}",
        CreatedAt      = DateTimeOffset.UtcNow.AddMonths(-6),
        UpdatedAt      = DateTimeOffset.UtcNow.AddDays(-1),
        Version        = 3
    };

    [Fact]
    public void AnonymizeCustomer_SetsNameToDeletedUser()
    {
        var customer = BuildCustomer();
        var now = DateTimeOffset.UtcNow;

        CustomerAnonymizer.Anonymize(customer, "abc123456789", now);

        Assert.Equal("Deleted", customer.FirstName);
        Assert.Equal("User",    customer.LastName);
        Assert.Equal("Deleted User", customer.DisplayName);
    }

    [Fact]
    public void AnonymizeCustomer_SetsPhoneTombstoneThatIsUnique()
    {
        // Use GUIDs that differ in the first 12 hex characters so the tombstoneId is distinct.
        var id1 = Guid.Parse("aabbccdd-1122-3344-5566-778899aabbcc");
        var id2 = Guid.Parse("11223344-aabb-ccdd-eeff-001122334455");

        var c1 = BuildCustomer(id1);
        var c2 = BuildCustomer(id2);
        var now = DateTimeOffset.UtcNow;

        CustomerAnonymizer.Anonymize(c1, id1.ToString("N")[..12], now);
        CustomerAnonymizer.Anonymize(c2, id2.ToString("N")[..12], now);

        // Both must start with +00del (reserved CC) and be distinct.
        Assert.StartsWith("+00del", c1.PhoneE164);
        Assert.StartsWith("+00del", c2.PhoneE164);
        Assert.NotEqual(c1.PhoneE164, c2.PhoneE164);
    }

    [Fact]
    public void AnonymizeCustomer_SetsEmailTombstoneThatIsUnique()
    {
        var id1 = Guid.Parse("aaaabbbb-cccc-dddd-eeee-ffff00001111");
        var id2 = Guid.Parse("bbbbcccc-dddd-eeee-ffff-000011112222");

        var c1 = BuildCustomer(id1);
        var c2 = BuildCustomer(id2);
        var now = DateTimeOffset.UtcNow;

        CustomerAnonymizer.Anonymize(c1, id1.ToString("N")[..12], now);
        CustomerAnonymizer.Anonymize(c2, id2.ToString("N")[..12], now);

        // Must end with @anon.invalid and be distinct.
        Assert.EndsWith("@anon.invalid", c1.Email!);
        Assert.EndsWith("@anon.invalid", c2.Email!);
        Assert.NotEqual(c1.Email, c2.Email);
    }

    [Fact]
    public void AnonymizeCustomer_ClearsBiographicFields()
    {
        var customer = BuildCustomer();
        CustomerAnonymizer.Anonymize(customer, "x", DateTimeOffset.UtcNow);

        Assert.Null(customer.Gender);
        Assert.Null(customer.DateOfBirth);
        Assert.Null(customer.AvatarUrl);
    }

    [Fact]
    public void AnonymizeCustomer_ClearsAllOptIns()
    {
        var customer = BuildCustomer();
        CustomerAnonymizer.Anonymize(customer, "x", DateTimeOffset.UtcNow);

        Assert.False(customer.MarketingOptIn);
        Assert.False(customer.SmsOptIn);
        Assert.False(customer.WhatsappOptIn);
        Assert.False(customer.EmailOptIn);
        Assert.False(customer.PushOptIn);
    }

    [Fact]
    public void AnonymizeCustomer_ClearsVerificationTimestamps()
    {
        var customer = BuildCustomer();
        customer.PhoneVerifiedAt = DateTimeOffset.UtcNow.AddMonths(-3);
        customer.EmailVerifiedAt = DateTimeOffset.UtcNow.AddMonths(-2);

        CustomerAnonymizer.Anonymize(customer, "x", DateTimeOffset.UtcNow);

        Assert.Null(customer.PhoneVerifiedAt);
        Assert.Null(customer.EmailVerifiedAt);
    }

    [Fact]
    public void AnonymizeCustomer_SetsStatusToDeleted()
    {
        var customer = BuildCustomer();
        var now = DateTimeOffset.UtcNow;

        CustomerAnonymizer.Anonymize(customer, "x", now);

        // Must match customers_status_check: active|blocked|deletion_requested|deleted
        Assert.Equal("deleted", customer.Status);
        Assert.Equal(now,       customer.DeletedAt);
    }

    [Fact]
    public void AnonymizeCustomer_BumpsVersionAndUpdatedAt()
    {
        var customer = BuildCustomer();
        var originalVersion   = customer.Version;
        var originalUpdatedAt = customer.UpdatedAt;
        var now = DateTimeOffset.UtcNow;

        CustomerAnonymizer.Anonymize(customer, "x", now);

        Assert.Equal(originalVersion + 1, customer.Version);
        Assert.Equal(now, customer.UpdatedAt);
        Assert.True(customer.UpdatedAt > originalUpdatedAt);
    }

    [Fact]
    public void AnonymizeCustomer_PreservesFinancialCounters()
    {
        var customer = BuildCustomer();
        customer.LifetimeOrders  = 42;
        customer.LifetimeSpend   = 9999.99m;
        customer.WalletBalance   = 100.00m;

        CustomerAnonymizer.Anonymize(customer, "x", DateTimeOffset.UtcNow);

        // Financial history is intentionally NOT wiped — GST 72-month retention.
        Assert.Equal(42,      customer.LifetimeOrders);
        Assert.Equal(9999.99m, customer.LifetimeSpend);
        Assert.Equal(100.00m, customer.WalletBalance);
    }

    [Fact]
    public void AnonymizeCustomer_TombstoneIdOf12CharsIsExactly()
    {
        var id = Guid.NewGuid();
        var tombstoneId = id.ToString("N")[..12];

        var customer = BuildCustomer(id);
        CustomerAnonymizer.Anonymize(customer, tombstoneId, DateTimeOffset.UtcNow);

        // Sanity: "+00del" (6) + 12 hex chars = 18 — fits phone_e164 varchar(20).
        Assert.Equal(12 + "+00del".Length, customer.PhoneE164.Length);
        Assert.True(customer.PhoneE164.Length <= 20,
            $"phone_e164 must fit varchar(20); got {customer.PhoneE164.Length} chars: '{customer.PhoneE164}'");
    }

    [Fact]
    public void AnonymizeCustomer_IsIdempotent_WhenCalledTwice()
    {
        // Calling AnonymizeCustomer on an already-erased customer must not throw
        // and the output state must be valid.
        var customer = BuildCustomer();
        var now = DateTimeOffset.UtcNow;
        var tombstoneId = customer.Id.ToString("N")[..12];

        CustomerAnonymizer.Anonymize(customer, tombstoneId, now);
        var phoneAfterFirst = customer.PhoneE164;

        // Second call with same tombstoneId must produce identical tombstone values.
        CustomerAnonymizer.Anonymize(customer, tombstoneId, now.AddSeconds(1));

        Assert.Equal(phoneAfterFirst, customer.PhoneE164);
        Assert.Equal("deleted", customer.Status);
    }
}
