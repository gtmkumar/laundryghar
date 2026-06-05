using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.SharedDataModel;

/// <summary>
/// Helpers for Development-only data seeding.
///
/// At runtime every service connects as the non-superuser <c>app_user</c> so that
/// PostgreSQL Row-Level Security is enforced (see harden_app_user_and_rls_bypass.sql).
/// Seeding, however, writes bootstrap rows across multiple brands BEFORE any HTTP
/// request has established a tenant context — under app_user those INSERTs would be
/// rejected by RLS WITH CHECK policies. Seeding is therefore a privileged
/// administrative task and must run on the <c>Admin</c> (postgres/superuser)
/// connection, which bypasses RLS natively. Seeding only ever runs in Development.
/// </summary>
public static class SeedingSupport
{
    /// <summary>
    /// Builds a standalone <see cref="LaundryGharDbContext"/> on the privileged admin
    /// connection, with NO RlsConnectionInterceptor (the superuser connection bypasses
    /// RLS natively). The caller owns the returned context and must dispose it.
    /// </summary>
    public static LaundryGharDbContext CreatePrivilegedContext(string adminConnectionString)
    {
        if (string.IsNullOrWhiteSpace(adminConnectionString))
            throw new InvalidOperationException(
                "An admin (postgres) connection string is required for seeding. " +
                "Set ConnectionStrings:Admin.");

        var options = new DbContextOptionsBuilder<LaundryGharDbContext>()
            .UseNpgsql(adminConnectionString, npgsql =>
            {
                npgsql.UseNetTopologySuite();
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .Options;

        return new LaundryGharDbContext(options);
    }
}
