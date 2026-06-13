using laundryghar.SharedDataModel.Contracts;

namespace laundryghar.Worker.Infrastructure;

/// <summary>
/// No-op ICurrentTenant for the background worker process.
/// The worker connects as the postgres superuser which bypasses PostgreSQL RLS natively;
/// this implementation additionally sets BypassRls = true so the RlsConnectionInterceptor
/// emits SET app.bypass_rls = 'true', making intent explicit and defensive.
/// There is intentionally no brand/franchise/store context — the worker must see all rows
/// across all tenants in order to drain the cross-brand outbox tables.
/// </summary>
internal sealed class WorkerCurrentTenant : ICurrentTenant
{
    public Guid? BrandId     => null;
    public Guid? FranchiseId => null;
    public Guid? StoreId     => null;
    public Guid? UserId      => null;
    public bool  BypassRls   => true;
}
