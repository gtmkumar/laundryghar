using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace laundryghar.SharedDataModel.Persistence;

/// <summary>
/// Design-time factory used by EF Core tooling (dotnet ef dbcontext info, scaffold, etc.).
/// Connects to the local development database.
/// NOTE: Never run "dotnet ef migrations add" or "database update" from this project —
/// the live DB schema is canonical and migrations are not generated from this library.
/// </summary>
public sealed class LaundryGharDbContextFactory : IDesignTimeDbContextFactory<LaundryGharDbContext>
{
    public LaundryGharDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LaundryGharDbContext>();

        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=laundry_ghar_db;Username=postgres;Password=postgres",
            npgsql => npgsql.UseNetTopologySuite());

        return new LaundryGharDbContext(optionsBuilder.Options);
    }
}
