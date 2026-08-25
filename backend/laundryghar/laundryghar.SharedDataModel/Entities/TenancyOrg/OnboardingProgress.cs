namespace laundryghar.SharedDataModel.Entities.TenancyOrg;

/// <summary>
/// The two pieces of wizard state that cannot be inferred from a provider's data (§9, migration
/// 0017). Everything else — whether each step is actually done — is derived from the real rows, so
/// the wizard cannot claim a location exists after it was deleted.
/// </summary>
public class OnboardingProgress
{
    public Guid BrandId { get; set; }

    /// <summary>"Not now." Nothing in the data reveals a decision to skip.</summary>
    public string[] SkippedSteps { get; set; } = [];

    /// <summary>Half-typed answers keyed by step, so closing the tab does not lose them. Never read
    /// to decide status.</summary>
    public string Draft { get; set; } = "{}";

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>§9's wizard steps, in the order the strategy lists them.</summary>
public static class OnboardingStep
{
    public const string Location = "location";
    public const string Catalog  = "catalog";
    public const string Staff    = "staff";
    public const string Payments = "payments";
    public const string GoLive   = "go_live";

    public static readonly string[] All = [Location, Catalog, Staff, Payments, GoLive];

    /// <summary>
    /// Steps a provider may defer. Location and catalogue are not on this list: a business with
    /// neither cannot take a single order, so letting someone skip past them would produce a
    /// "finished" setup that does nothing. Going live is not skippable either — it IS the finish
    /// line.
    /// </summary>
    public static readonly string[] Skippable = [Staff, Payments];
}
