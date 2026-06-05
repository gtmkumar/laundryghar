---
name: project-aspire-apphost
description: Aspire 13.4.2 AppHost setup choices, SDK quirks, and connection string injection pattern for the LaundryGhar backend
metadata:
  type: project
---

Aspire orchestration added to the LaundryGhar backend. 13 projects (11 original + ServiceDefaults + AppHost) all build with 0 errors.

**Why Aspire.AppHost.Sdk/13.4.2 (not 9.x):**
- .NET 10 SDK 10.0.103 deprecated the Aspire workload manifest (NETSDK1228 on any project with IsAspireHost=true)
- Aspire 9.x SDK sets DCP/Dashboard references only for FileBasedProgram=true projects; standard Exe projects don't get the run targets → ComputeRunArguments missing → dotnet run fails
- Aspire 13.4.2 SDK unconditionally adds DCP/Dashboard references via AddImplicitAspireAppHostPackage → dotnet run works for both file-based and Exe projects

**Why NOT Aspire.AppHost.Sdk/13.x with IsAspireHost or PackageReference (the 9.x way):**
- The SDK attribute `Sdk="Aspire.AppHost.Sdk/13.4.2"` on the Project element correctly wraps Microsoft.NET.Sdk once
- The project references MUST be in sibling directories (not subdirectories of AppHost) because Microsoft.NET.Sdk's default **/*.cs glob would pull in sibling service source files otherwise → CS8802 duplicate top-level statements

**Connection string injection:**
- `AddConnectionString("name")` creates a `parameter.v0` resource with `secret:true` — DCP waits for external value resolution and never starts services
- Correct approach: read connection string from AppHost's own config (`builder.Configuration["ConnectionStrings:Default"]`) and inject as literal `WithEnvironment("ConnectionStrings__Default", connStr)`
- Services read `ConnectionStrings:Default`; the env var `ConnectionStrings__Default` maps to that key via ASP.NET Core config conventions

**ASPIRE002 warning:**
- Cosmetic only — fires because the 'Aspire' ProjectCapability (from Aspire.Hosting.AppHost.targets) is evaluated after the SDK's check BeforeTargets="PrepareForBuild"
- Build succeeds; runtime works; ignore this warning

**Port pinning:**
- All 9 services pinned via `.WithHttpEndpoint(port: N, name: "http")`
- DCP registers the ports; services launch with `HTTP_PORTS=N` injected by Aspire (overrides their launchSettings/appsettings URLs)
- macOS warning "Could not use the same port for all addresses" on dual-stack is benign — DCP uses 127.0.0.1 when IPv6 binding fails

**How to apply:**
- Use `Aspire.AppHost.Sdk/13.4.2` as the sole Sdk attribute on AppHost project
- Use `builder.Configuration["ConnectionStrings:Default"]` to read the DB connection string in AppHost, then `WithEnvironment` to inject it
- Do NOT use AddConnectionString() for external (pre-existing) databases

**Run command:** `ASPNETCORE_ENVIRONMENT=Development dotnet run --project laundryghar.AppHost`
Dashboard URL is printed to stdout with a one-time login token.
