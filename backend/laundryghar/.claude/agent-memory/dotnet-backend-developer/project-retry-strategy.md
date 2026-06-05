---
name: project-retry-strategy
description: NpgsqlRetryingExecutionStrategy incompatibility with user-initiated transactions; fix pattern for Finance and all services
metadata:
  type: project
---

`AddSharedDataModel` registers Npgsql with `EnableRetryOnFailure(3)`, which installs `NpgsqlRetryingExecutionStrategy`. This strategy rejects `BeginTransactionAsync` called directly on `_db.Database` — it throws `InvalidOperationException` at runtime with message "does not support user-initiated transactions".

**Fix pattern**: wrap all transactional code in `_db.Database.CreateExecutionStrategy().ExecuteAsync(async () => { ... })`:

```csharp
var strategy = _db.Database.CreateExecutionStrategy();
await strategy.ExecuteAsync(async () =>
{
    await using var tx = await _db.Database.BeginTransactionAsync(ct);
    // ... work ...
    await _db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
});
```

**Why:** BC-7 Finance crashed on cash book entry creation (HTTP 500) because `AddCashBookEntryHandler` called `BeginTransactionAsync` directly. Discovered during acceptance testing.

**How to apply:** Any handler in any service that opens a transaction manually must use this pattern. One-off `SaveChangesAsync` (no explicit transaction) is fine and does not require the strategy wrapper.
