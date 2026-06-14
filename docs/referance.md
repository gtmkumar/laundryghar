# NXGN Portal API — Architecture Guide

A **.NET 6 Clean Architecture** web API for an HR / employee management portal.
This document explains the project structure and conventions so any feature in
the codebase can be navigated by understanding a single repeating pattern.

---

## The Big Picture

The `src` folder holds the 5 core projects, layered as concentric rings.
Dependencies point **inward only** — outer layers know about inner layers, never
the reverse.

```
WebApi ──▶ Infrastructure ──▶ Application ──▶ Repository ──▶ Domain
  │                                                            ▲
  └──────────────── nxgn.utilities (shared, used everywhere) ─┘
```

| Project                        | Role                                                                                           | Depends on        |
| ------------------------------ | ---------------------------------------------------------------------------------------------- | ----------------- |
| **nxgn.portal.Domain**         | Entities, enums, base classes, domain events. No business logic. The center.                   | nothing           |
| **nxgn.portal.Repository**     | Data-access interfaces + EF Core implementations, request/response DTOs, Unit of Work          | Domain, utilities |
| **nxgn.portal.Application**    | Business logic via **CQRS / MediatR** — Commands & Queries per feature, validators, AutoMapper | Repository        |
| **nxgn.portal.Infrastructure** | `DbContext`, EF interceptors (auditing, domain events), services (CurrentUser, DateTime)       | Application       |
| **nxgn.portal.WebApi**         | Entry point — Controllers, `Program.cs`, JWT auth, Swagger, middleware                         | all of the above  |

`NxgnJob.Utilities` (folder `nxgn.utilities`) is the **shared kernel** —
`Response<T>` wrappers, exceptions, middleware, `Result` / `ResultCode`, email
service, `PaginatedList`.

---

## How a Request Flows

This is the key pattern repeated everywhere — learn it once and every feature
reads the same.

```
HTTP request
  → Controller (BaseApiController) — just dispatches via MediatR, no logic
  → MediatR pipeline behaviors: Validation → Performance → Authorization
  → Command/Query Handler (Application layer) — the actual business logic
  → Repository (EF Core) — queries DB, projects entities into ResponseModels
  → DbContext → SQL Server
  → Response<T> envelope back to the client
```

---

## The Repeating Folder Pattern

Every feature/entity (Employee, Country, Leave, Project…) follows the **same
shape**, so once you understand one you understand all ~45.

Example — the **Employee** feature:

- `Domain/Entities/Employee.cs` → entity, inherits `BaseAuditableEntity`
- `Application/Employees/Commands/CreateEmployeeCommand.cs` → command + handler + validator
- `Application/Employees/Queries/GetEmployeesQuery.cs` → query + handler
- `Repository/IRepositories/IEmployeeRepository.cs` → data-access interface
- `Repository/Repositories/EmployeeRepository.cs` → EF Core implementation
- `Repository/ResponseModel/EmployeeResponseModel.cs` → DTO returned to clients
- `WebApi/Controllers/EmployeeController.cs` → HTTP endpoints

---

## Project Details

### nxgn.portal.Domain

- **Base classes**: `BaseEntity` (domain events), `BaseAuditableEntity`
  (adds `CreatedOn/By`, `UpdatedOn/By`, `Status`), `BaseEvent` (MediatR
  `INotification`), `ValueObject`.
- **Entities/** — 50+ rich domain entities (`Employee`, `Company`, `Department`,
  `Project`, `LeaveDetail`, plus lookup tables like `Gender`, `MaritalStatus`).
- **Events/** — e.g. `CreateEmployeeEvent`.
- Packages: `MediatR`, `Microsoft.EntityFrameworkCore.SqlServer`.

### nxgn.portal.Repository

- **Core/** — generic `IRepository<TEntity>` (Result-wrapped CRUD) and `IUnitOfWork`.
- **Common/Interfaces/** — `IRepository<TIn, TOut>` (entity in, DTO out), `INxgnPortalDbContext`, `ICurrentUserService`.
- **Persistence/** — generic `Repository<TEntity>` + `UnitOfWork<TContext>` implementations.
- **IRepositories/** + **Repositories/** — 50+ feature-specific interfaces and implementations.
- **RequestModel/** — filter/pagination DTOs (base `FilterRequestModel`).
- **ResponseModel/** — 90+ response DTOs.
- **DependencyInjection.cs** — `AddRepository()` registers all repositories.

### nxgn.portal.Application

- Feature folders, each with **Commands/** and **Queries/** (CQRS via MediatR).
- **Common/Behaviours/** — pipeline: `ValidationBehaviour`, `PerformanceBehaviour`, `AuthorizationBehaviour`, `LoggingBehaviour`.
- **Common/Mappings/** — AutoMapper profiles (auto-discovery via `IMapFrom<T>`).
- Validators use **FluentValidation** (`<X>CommandValidator`).
- Packages: `AutoMapper`, `FluentValidation`, `MediatR`.

### nxgn.portal.Infrastructure

- **Persistence/NxgnPortalDbContext.cs** — EF Core context, 50+ `DbSet`s, Fluent API in `OnModelCreating`. Supports SQL Server (primary) and MySQL (conditional via `IsMySql`).
- **Data/Interceptors/** — `AuditableEntityInterceptor` (auto audit fields), `DispatchDomainEventsInterceptor` (publishes domain events to MediatR).
- **Services/** — `CurrentUserService`, `DateTimeService`, `DomainEventService`.

### nxgn.portal.WebApi

- **Controllers/** — 47 controllers, all extend `BaseApiController` (exposes `Mediator`).
- **Program.cs** — wires `AddApplication()` + `AddInfrastructure()`, JWT bearer auth, CORS (NXGN domains whitelisted), Swagger, health checks, custom `ExceptionHandler` middleware.
- **appsettings.json** — connection strings, JWT secret, email/SMTP, document paths.

---

## Conventions to Know

- **Naming**: `Create/Update/Delete<X>Command`, `Get<X>Query`, `I<X>Repository`, `<X>ResponseModel`.
- **Auditing is automatic** — `AuditableEntityInterceptor` fills `CreatedBy/On`, `UpdatedBy/On` on save. Don't set these manually.
- **Multi-tenancy** — repositories filter by `_currentUserService.CurrentCompanyId`. Most queries also filter `Status == true` (soft delete).
- **Auth** — JWT bearer; controllers use `[Authorize(Roles = "Admin,Employee,Manager")]`. User context comes from JWT claims via `ICurrentUserService`.
- **Two repository styles coexist**: generic `IRepository<TEntity>` (Result-wrapped CRUD) and feature-specific `IRepository<TIn, TOut>` (entity in, DTO out).
- **Standardized responses** — all endpoints return `Response<T>` envelopes from `nxgn.utilities`.

---

## Other Projects (outside `src`)

| Project                                                 | Purpose                                                                                                                   |
| ------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| **nxgn.portal.database**                                | SQL Server database project (`.sqlproj`). "load failed" in the editor just means SSDT tooling isn't installed — harmless. |
| **nxgn-auth** (`nxgn.auth.domain` + `nxgn.auth.webapi`) | Current standalone authentication service.                                                                                |
| **auth.src** / **authsrc**                              | Older/legacy auth attempts, superseded by `nxgn-auth`.                                                                    |
| **WebApp** (repo root)                                  | React / Redux frontend that consumes these APIs.                                                                          |

---

## Mental Model

> **Domain = nouns** · **Application = verbs (via MediatR)** · **Repository = data** · **Infrastructure = plumbing** · **WebApi = the door.**

Pick any one feature folder and it's a template for all the others.
