using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.CustomerCatalog;

/// <summary>
/// A federated sign-in link between a customer and an external identity provider
/// (customer_catalog.customer_identities).
///
/// Keyed on <see cref="ProviderUid"/> — the provider's immutable subject — rather than
/// email, because a Google account's email address can change while its subject cannot.
/// Email is stored only so an existing phone-registered customer can be matched on first
/// Google sign-in.
/// </summary>
public class CustomerIdentity
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid BrandId { get; set; }

    /// <summary>One of: google, apple, facebook (DB CHECK constraint).</summary>
    public string Provider { get; set; } = null!;

    /// <summary>The provider's immutable subject identifier (Google/Apple <c>sub</c>).</summary>
    public string ProviderUid { get; set; } = null!;

    public string? Email { get; set; }
    public bool EmailVerified { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations
    public Customer Customer { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
}

/// <summary>Values allowed by the customer_identities.provider CHECK constraint.</summary>
public static class CustomerIdentityProvider
{
    public const string Google = "google";
    public const string Apple = "apple";
    public const string Facebook = "facebook";
}
