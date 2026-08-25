namespace laundryghar.SharedDataModel.Entities.IdentityAccess;

/// <summary>
/// A provider's recorded, time-boxed consent for one named support engineer to work inside their
/// account (§7 "impersonation with consent + full audit", §8.1 "not without recorded consent").
///
/// <para>The lifecycle is deliberately two-sided: support <b>requests</b>, the provider
/// <b>approves</b>. The two permissions behind those verbs are held by disjoint roles, asserted in
/// migration 0014 — consent the grantee can grant itself is not consent.</para>
/// </summary>
public class ImpersonationGrant
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }

    /// <summary>The single named person let in. Never a role.</summary>
    public Guid SupportUserId { get; set; }

    /// <summary>Why they are asking. Shown to the owner making the decision, and kept afterwards.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary><see cref="ImpersonationScope"/>. Defaults to read-only; write is a separate answer.</summary>
    public string Scope { get; set; } = ImpersonationScope.ReadOnly;

    /// <summary><see cref="ImpersonationGrantStatus"/>.</summary>
    public string Status { get; set; } = ImpersonationGrantStatus.Pending;

    public DateTimeOffset RequestedAt { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>Hard end of the session. Never null once approved (enforced by CHECK), never more
    /// than 24h after approval.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public string? RevokeReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>How much a consented session may do. Read-first is the §7 default.</summary>
public static class ImpersonationScope
{
    public const string ReadOnly  = "read_only";
    public const string ReadWrite = "read_write";
}

/// <summary>Where a grant is in its life. `expired` is reported by the state oracle rather than
/// stored — time passing must end a session by itself, not because a sweep job remembered to.</summary>
public static class ImpersonationGrantStatus
{
    public const string Pending  = "pending";
    public const string Approved = "approved";
    public const string Denied   = "denied";
    public const string Revoked  = "revoked";
    public const string Expired  = "expired";
}
