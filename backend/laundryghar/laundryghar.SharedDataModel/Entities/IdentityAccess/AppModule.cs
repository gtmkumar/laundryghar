namespace laundryghar.SharedDataModel.Entities.IdentityAccess;

/// <summary>Navigator/module registry (identity_access.modules). Drives both the
/// admin sidebar menu and the Roles &amp; Permissions matrix rows. permission_modules
/// maps the raw permissions.module values onto each UI module.</summary>
public class AppModule
{
    public Guid Id { get; set; }
    public string Key { get; set; } = null!;
    public string Label { get; set; } = null!;
    public string? Icon { get; set; }
    public string? Route { get; set; }
    public string? Section { get; set; }
    public int NavOrder { get; set; }
    public int MatrixOrder { get; set; }
    public bool ShowInNav { get; set; }
    public bool ShowInMatrix { get; set; }
    public string? RequiredPermission { get; set; }
    public string[] PermissionModules { get; set; } = [];
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
