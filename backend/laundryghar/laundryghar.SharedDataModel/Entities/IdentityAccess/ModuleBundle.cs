namespace laundryghar.SharedDataModel.Entities.IdentityAccess;

/// <summary>Platform catalogue of plan→module bundles (identity_access.module_bundle).
/// Used to expand into per-brand <see cref="BrandModule"/> rows at onboarding / plan
/// change. Global (no RLS), like <see cref="AppModule"/>.</summary>
public class ModuleBundle
{
    public string Code { get; set; } = null!;   // 'starter','pro','enterprise'
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public ICollection<ModuleBundleItem> Items { get; set; } = new List<ModuleBundleItem>();
}

/// <summary>A module included in a <see cref="ModuleBundle"/> (identity_access.module_bundle_item).</summary>
public class ModuleBundleItem
{
    public string BundleCode { get; set; } = null!;
    public string ModuleKey { get; set; } = null!;
}
