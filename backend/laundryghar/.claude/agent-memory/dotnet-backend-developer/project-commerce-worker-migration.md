---
name: project-commerce-worker-migration
description: Commerce Worker lane + HostTenant migration into commerce.WebApi:5242 — no-DB hosted-service gate, CommerceHostCurrentTenant tenant swap, FrameworkReference need, MatviewRefresh pulled in
metadata:
  type: project
---

Worker lane (13 BackgroundServices + channel senders + abstractions + options + WorkerScope/CommerceHostCurrentTenant) migrated into commerce.WebApi:5242. Background services only — no REST endpoints. Completes [[project-commerce-core-migration]] / [[project-commerce-finance-migration]] / [[project-commerce-analytics-migration]]. Non-derivable decisions:

**All worker code lives in `commerce.Infrastructure/Worker/`** (ns `commerce.Infrastructure.Worker[.Services|.Channels|.Abstractions|.Options|.Stubs]`). HostTenant (`WorkerScope`, `CommerceHostCurrentTenant`) sits directly under `Worker/`. Workers resolve the CONCRETE `LaundryGharDbContext` from a per-tick scope (not `ICommerceDbContext`) — infra may use the concrete context, and no new DbSets were added.

**No-DB hosted-service gate.** Every `AddHostedService` opens a DbContext per tick, so the ENTIRE block is wrapped in `if (!string.IsNullOrWhiteSpace(connStr))` (the `ConnectionStrings:Default` already read for AddSharedDataModel). When empty: register NONE, and after `builder.Build()` emit `app.Logger.LogWarning("Commerce workers skipped: no ConnectionStrings:Default configured…")`. The warning is logged POST-build (no ILogger before build). The channel-sender/options/HTTP-client/charger DI stays UNCONDITIONAL (no DB at startup). Also `Configure<HostOptions>(o => o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore)` as defence-in-depth. Mirrors the IdentitySeeder gate in core.

**ICurrentTenant swapped to CommerceHostCurrentTenant.** The host normally registers `HttpContextCurrentTenant` via `AddCurrentTenant()`. For the worker lane, Program.cs instead does `AddScoped<ICurrentTenant, CommerceHostCurrentTenant>()` (and keeps `AddCurrentUser()`, which already calls `AddHttpContextAccessor`). `CommerceHostCurrentTenant` dispatches: HTTP scope → JWT-claim tenant (RLS enforced); worker scope (positive `WorkerScope.IsWorkerScope` AsyncLocal marker set by `CreateWorkerAsyncScope()`/`CreateWorkerScope()`) → BypassRls=true. Do NOT register the no-HttpContext-inference version — fail-closed SEC-1. `WorkerCurrentTenant` was ported for parity but is NOT wired in.

**commerce.Infrastructure needed `<FrameworkReference Include="Microsoft.AspNetCore.App" />`** — the library now uses `BackgroundService`/`IHostedService` (Hosting.Abstractions) + `IHttpContextAccessor`/`HttpMethods` (Http.Abstractions). The transitive Utilities framework ref was not relied upon; added explicitly.

**Types made public (were `internal` in legacy single-assembly).** DevSubscriptionCharger, LoggingChannelSender, LoggingEventPublisher, NotificationSettingsCache, RoutingChannelSender + the 3 cloud senders — all registered/`new`-ed from Program.cs (a different assembly now), so `internal` would not bind. Same pattern as `SettingsFirstPaymentGateway` in [[project-commerce-core-migration]].

**Bare `SharedDataModel.Entities.*` had to be fully-qualified to `laundryghar.SharedDataModel.Entities.*`** in DailyReconService + LoyaltyEarnService (4 sites). Legacy `laundryghar.Commerce.*` namespace made the bare prefix resolve; `commerce.Infrastructure.Worker.Services` does not. Other services already used the qualified form.

**MatviewRefreshService was pulled in here, not with Analytics.** The Analytics slice DEFERRED it; it's a hosted worker, so it belongs under the same no-DB gate. Ported into `Worker/Services/` and registered first in the gated block. `RiderLoadHelper` (AutoDispatch) lives in `laundryghar.SharedDataModel.Logistics` (shared, no port needed).

**Channel-sender unused `using System.Net.Http.Headers;`** dropped from WhatsApp/Msg91 senders (legacy had it but never referenced AuthenticationHeaderValue in the body — those headers are set on the named HttpClient in Program.cs).

Boot smoke (no DB, port 5242): host BOOTS, workers-skipped warning present, openapi 200, root 200, Analytics/Finance/Commerce admin routes 401 (no regression), no DI/resolution/startup errors. Build 0 errors (8 pre-existing NU1510 nuget-prune warnings only).
