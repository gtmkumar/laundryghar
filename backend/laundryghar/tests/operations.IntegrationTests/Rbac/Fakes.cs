using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.Auth;
using laundryghar.Utilities.Services;

namespace operations.IntegrationTests.Rbac;

/// <summary>Deterministic <see cref="ICurrentTenant"/> for the audit interceptor: brand_id is stamped
/// on every audit row from THIS (not the mutated entity), proving docs/rbac.md §12 tenant provenance.</summary>
public sealed class FakeCurrentTenant : ICurrentTenant
{
    public Guid? BrandId { get; init; }
    public Guid? FranchiseId { get; init; }
    public Guid? StoreId { get; init; }
    public Guid? UserId { get; init; }
    public bool BypassRls { get; init; }
}

/// <summary>Deterministic <see cref="ICurrentUser"/> — supplies the actor stamped onto audit rows and
/// satisfies the interceptor ctor. Only <see cref="UserId"/>/<see cref="Email"/>/<see cref="Phone"/>
/// are read by <c>AuditContext.Fill</c>; the rest are inert defaults.</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; init; }
    public string? UserType { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public Guid? BrandId { get; init; }
    public Guid? FranchiseId { get; init; }
    public Guid? StoreId { get; init; }
    public string? ScopeType { get; init; }
    public Guid? ScopeId { get; init; }
    public bool IsAuthenticated => UserId is not null;
    public bool IsPlatformAdmin { get; init; }

    public bool HasPermission(string permissionCode) => false;

    public IReadOnlyCollection<ScopeNode> ScopeNodes { get; init; } = Array.Empty<ScopeNode>();

    public bool IsWithinScope(Guid? brandId = null, Guid? franchiseId = null, Guid? storeId = null, Guid? warehouseId = null)
        => true;

    public Guid? TryGetBrandId() => BrandId;

    public Guid RequireBrandId() => BrandId ?? throw new UnauthorizedAccessException("no brand");
}
