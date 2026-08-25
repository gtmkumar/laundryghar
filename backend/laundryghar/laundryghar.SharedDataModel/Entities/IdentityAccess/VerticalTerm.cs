namespace laundryghar.SharedDataModel.Entities.IdentityAccess;

/// <summary>One user-facing word, per vertical (identity_access.vertical_terms).
/// PLATFORM_STRATEGY.md §3: "terminology is config, not code".</summary>
public class VerticalTerm
{
    public string VerticalKey { get; set; } = null!;
    /// <summary>Stable identifier the clients ask for — <c>item</c>, <c>booking</c>,
    /// <c>onsite_location</c>. Never shown to a user.</summary>
    public string TermKey { get; set; } = null!;
    public string Singular { get; set; } = null!;
    public string? Plural { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
