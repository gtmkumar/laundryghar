namespace laundryghar.SharedDataModel.Entities.TenancyOrg;

/// <summary>
/// A provider's wind-down (§9 "export offered, wind-down retention, then deletion per DPDP").
///
/// <para>The record outlives the event on purpose: a withdrawn cancellation and a completed purge
/// both stay, because "did they ever try to leave, and did we actually delete it" are questions that
/// get asked long afterwards.</para>
/// </summary>
public class BrandCancellation
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }

    public Guid? RequestedByUserId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>When the data may be destroyed. Never null — a wind-down with no end is not one.</summary>
    public DateTimeOffset RetentionUntil { get; set; }

    /// <summary><see cref="BrandCancellationStatus"/>.</summary>
    public string Status { get; set; } = BrandCancellationStatus.Retention;

    public DateTimeOffset? WithdrawnAt { get; set; }
    public Guid? WithdrawnByUserId { get; set; }
    public DateTimeOffset? PurgedAt { get; set; }

    /// <summary>Proof the "export offered" step happened, rather than being a checkbox in a UI.</summary>
    public int ExportCount { get; set; }
    public DateTimeOffset? LastExportedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public static class BrandCancellationStatus
{
    /// <summary>The window is running. Operations frozen; login, billing and export still open.</summary>
    public const string Retention = "retention";

    /// <summary>The window elapsed and the data is gone.</summary>
    public const string Purged = "purged";

    /// <summary>They changed their mind. Allowed right up to the purge.</summary>
    public const string Withdrawn = "withdrawn";
}
