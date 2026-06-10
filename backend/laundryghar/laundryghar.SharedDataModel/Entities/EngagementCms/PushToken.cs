using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.EngagementCms;

/// <summary>
/// Expo push notification token for a customer or staff/rider device
/// (engagement_cms.push_tokens).
/// Task #6 creates the table and sender; Task #7 adds the registration endpoints.
/// Append-only style — has created_at, updated_at; no deleted_at.
/// </summary>
public class PushToken
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }

    /// <summary>CHECK: customer, rider, staff.</summary>
    public string UserType { get; set; } = null!;

    /// <summary>Cross-BC scalar FK to customer_catalog.customers.id — no EF navigation.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>Cross-BC scalar FK to identity_access.users.id — no EF navigation.</summary>
    public Guid? UserId { get; set; }

    /// <summary>CHECK: ios, android, web.</summary>
    public string Platform { get; set; } = null!;

    public string Token { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
}
