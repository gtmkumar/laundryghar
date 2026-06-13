// Project-wide global usings for the consolidated Commerce host.
//
// HostTenant is exposed globally so the Worker BackgroundServices (namespace
// laundryghar.Worker.*) and the Analytics MatviewRefreshService can call the
// CreateWorkerAsyncScope()/CreateWorkerScope() extension methods that grant the
// fail-closed worker RLS-bypass marker (SEC-1). See HostTenant/WorkerScope.cs.
global using laundryghar.Commerce.HostTenant;
