namespace laundryghar.SharedDataModel.Entities.IdentityAccess;

/// <summary>
/// A machine credential for a brand (§11 P4, migration 0016). The secret itself is never stored —
/// only its Argon2id hash — so a database dump is not a set of working credentials.
/// </summary>
public class ApiKey
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>The PUBLIC half — <c>lg_live_a1b2c3d4</c>. Indexed, unique, and safe to display.</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    public string SecretHash { get; set; } = string.Empty;
    public string Environment { get; set; } = ApiKeyEnvironment.Live;

    /// <summary>Empty by default — a key with no scopes authenticates and does nothing. Issuing a
    /// credential that can read a whole business because nobody chose scopes is how integrations
    /// become breaches.</summary>
    public string[] Scopes { get; set; } = [];

    public string Status { get; set; } = ApiKeyStatus.Active;

    /// <summary>Null means "use the host default", never "unlimited".</summary>
    public int? RateLimitPerMinute { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public static class ApiKeyStatus
{
    public const string Active = "active";
    public const string Revoked = "revoked";
}

public static class ApiKeyEnvironment
{
    public const string Live = "live";
    public const string Test = "test";
}

/// <summary>Today's usage rollup for one key. A daily row, not a row per request.</summary>
public class ApiKeyUsage
{
    public Guid ApiKeyId { get; set; }
    public Guid BrandId { get; set; }
    public DateOnly UsageDate { get; set; }
    public long RequestCount { get; set; }
    public long ErrorCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
