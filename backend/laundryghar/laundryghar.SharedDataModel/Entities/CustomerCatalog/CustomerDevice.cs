namespace laundryghar.SharedDataModel.Entities.CustomerCatalog;

/// <summary>Mobile/web device registered by a customer (customer_catalog.customer_devices).
/// Has created_at, created_by only — no updated_at, no version, no deleted_at.</summary>
public class CustomerDevice
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid BrandId { get; set; }
    public string DeviceId { get; set; } = null!;
    public string Platform { get; set; } = null!;
    public string? OsVersion { get; set; }
    public string? DeviceModel { get; set; }
    public string? DeviceName { get; set; }
    public string? AppVersion { get; set; }
    public string? AppBuild { get; set; }
    public string? FcmToken { get; set; }
    public string? ApnsToken { get; set; }
    public bool PushEnabled { get; set; }
    public string? Language { get; set; }
    public string? Timezone { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public bool IsActive { get; set; }
    public string Metadata { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigation
    public Customer Customer { get; set; } = null!;
}
