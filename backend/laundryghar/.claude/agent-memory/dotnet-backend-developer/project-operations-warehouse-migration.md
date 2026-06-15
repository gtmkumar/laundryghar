---
name: project-operations-warehouse-migration
description: operations.* split-host conventions learned migrating Warehouse from legacy MediatR — namespace collision, transaction seam, storage provider migration
metadata:
  type: project
---

Migrating legacy MediatR slices into the operations split host (operations.WebApi:5015, IOperationsDbContext over LaundryGharDbContext). Three non-derivable gotchas:

**1. `Warehouse` entity name collides with the `operations.Application.Warehouse` namespace.**
**Why:** sub-domain folders live under `operations.Application.Warehouse.*`, so in `IOperationsDbContext` (namespace `operations.Application.Common.Interfaces`) the bare `DbSet<Warehouse>` resolves to the namespace, not the TenancyOrg entity → CS0118.
**How to apply:** fully-qualify as `DbSet<laundryghar.SharedDataModel.Entities.TenancyOrg.Warehouse>` in the interface. Same pattern for the `StockReconciliation` entity vs the `...Warehouse.StockReconciliation` namespace — fully-qualify the entity type in handlers/DTOs of that slice.

**2. Explicit transactions: handlers can't touch the concrete DbContext.**
**Why:** legacy handlers did `_db.Database.CreateExecutionStrategy().ExecuteAsync(... BeginTransactionAsync ...)`, but IOperationsDbContext exposes no `Database`. NpgsqlRetryingExecutionStrategy still requires the strategy-owns-transaction wrapper (see [[project-retry-strategy]]).
**How to apply:** added `Task ExecuteInTransactionAsync(Func<CancellationToken,Task> action, CancellationToken ct)` to IOperationsDbContext; impl in OperationsDbContext does the CreateExecutionStrategy + BeginTransaction + Commit. Handlers add entities + SaveChangesAsync INSIDE the action. Only QC create needed it.

**3. Validators target the Request type, not the Command (Core convention).**
**Why:** endpoints bind the request body; `ValidationFilter<TRequest>` needs a bound arg of that type. Legacy validators were `AbstractValidator<XCommand>` referencing `x.Request.Foo`.
**How to apply:** convert to `AbstractValidator<XRequest>`, strip the `.Request.` prefix, drop route-bound rules (e.g. `x.Id`). EXCEPTION: an endpoint that binds individual params (IFormFile + query params, no request object — e.g. inspection photo upload) keeps its command-targeted validator but gets NO ValidationFilter (no bound arg to validate); the legacy ValidationPipelineBehavior that ran it is superseded/skipped.

**Storage:** legacy `laundryghar.ServiceDefaults.Storage` (IFileStorageProvider/LocalFileStorageProvider/FileStorageKeyGenerator) does NOT exist in the split repo. Migrated as: interface + pure FileStorageKeyGenerator into operations.Application/Common; LocalFileStorageProvider + LocalStorageOptions + factory into operations.Infrastructure/Storage; registered in AddOperationsInfrastructure(IConfiguration). Bind options via `Configure<T>(Action)` lambda reading `config["Storage:Local:RootPath"]`, NOT `Configure<T>(IConfiguration section)` — the reflection overload trips IL2026/IL3050 in the IsAotCompatible operations.Infrastructure project.

ActorId (Guid?) STAYS on command records; endpoint passes `user.UserId`. Handlers still inject ICurrentUser for `RequireBrandId()`.
