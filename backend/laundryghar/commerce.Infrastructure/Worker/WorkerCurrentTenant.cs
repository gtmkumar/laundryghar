using laundryghar.SharedDataModel.Contracts;

namespace commerce.Infrastructure.Worker;

/// <summary>
/// No-op ICurrentTenant for the background worker process.
/// The worker connects as the postgres superuser which bypasses PostgreSQL RLS natively;
/// this implementation additionally sets BypassRls = true so the RlsConnectionInterceptor
/// emits SET app.bypass_rls = 'true', making intent explicit and defensive.
/// There is intentionally no brand/franchise/store context — the worker must see all rows
/// across all tenants in order to drain the cross-brand outbox tables.
///
/// NOTE: in the consolidated commerce host the registered <see cref="ICurrentTenant"/> is the
/// dispatching <see cref="CommerceHostCurrentTenant"/>, which already yields worker semantics
/// inside a <see cref="WorkerScope"/>. This standalone no-op tenant is retained for parity with
/// the legacy standalone Worker process and is NOT wired into the host.
/// </summary>
public sealed class WorkerCurrentTenant : ICurrentTenant
{
    public Guid? BrandId     => null;
    public Guid? FranchiseId => null;
    public Guid? StoreId     => null;
    public Guid? UserId      => null;
    public Guid? PartnerId   => null;
    public bool  BypassRls   => true;
}
