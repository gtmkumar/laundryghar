using laundryghar.SharedDataModel.Persistence;
using laundryghar.Utilities.Auth;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Infrastructure.Persistence;

namespace operations.Tests.Catalog.Import;

/// <summary>Shared scaffolding for the catalog-import handler tests: an in-memory operations context
/// and a scriptable <see cref="ICurrentUser"/>. The parser tests need no DB and use neither.</summary>
internal static class ImportTestSupport
{
    /// <summary>Path to the real legacy rate-list workbook copied next to the test binaries.</summary>
    public static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "RateListForDrycleaning.xlsx");

    /// <summary>Fresh isolated in-memory operations context. Returns the adapter (what handlers take)
    /// plus the underlying context (for direct seeding / assertions).</summary>
    public static (IOperationsDbContext Db, LaundryGharDbContext Raw) NewDb()
    {
        var options = new DbContextOptionsBuilder<LaundryGharDbContext>()
            .UseInMemoryDatabase($"import-tests-{Guid.NewGuid():N}")
            .EnableSensitiveDataLogging()
            .Options;
        var raw = new LaundryGharDbContext(options);
        return (new OperationsDbContext(raw), raw);
    }

    public sealed class FakeCurrentUser : ICurrentUser
    {
        public FakeCurrentUser(Guid brandId, bool withinScope = true)
        {
            BrandId = brandId;
            UserId = Guid.NewGuid();
            WithinScope = withinScope;
        }

        public bool WithinScope { get; set; }

        public Guid? UserId { get; }
        public string? UserType => "staff";
        public string? Email => "importer@test.local";
        public string? Phone => null;
        public Guid? BrandId { get; }
        public Guid? FranchiseId => null;
        public Guid? StoreId => null;
        public string? ScopeType => "brand";
        public Guid? ScopeId => BrandId;
        public bool IsAuthenticated => true;
        public bool IsPlatformAdmin => false;
        public bool HasPermission(string permissionCode) => true;
        public IReadOnlyCollection<ScopeNode> ScopeNodes => [];
        public bool IsWithinScope(Guid? brandId = null, Guid? franchiseId = null, Guid? storeId = null, Guid? warehouseId = null) => WithinScope;
        public Guid? TryGetBrandId() => BrandId;
        public Guid RequireBrandId() => BrandId ?? throw new UnauthorizedAccessException("No brand.");
    }
}
