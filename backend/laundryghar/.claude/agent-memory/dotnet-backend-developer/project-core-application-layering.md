---
name: project-core-application-layering
description: core.Application must not reference core.Infrastructure — interfaces and dependency-free helpers consumed by handlers live in Application, not Infrastructure
metadata:
  type: project
---

`core.Application` references only `core.Domain` + `laundryghar.Utilities` — NOT `core.Infrastructure`.

**Why:** Clean-architecture dependency rule. Application handlers inject abstractions; concretes are wired in Infrastructure. When porting MediatR handlers from `laundryghar.Core` (legacy, single-project) into the split, any type a handler *calls statically* must also be reachable from Application.

**How to apply:**
- `IPasswordHasher` interface lives in `core.Application/Common/Interfaces/`; `Argon2PasswordHasher` impl stays in `core.Infrastructure/Auth/`. Password/invite handlers inject the interface, never the concrete.
- `EmailTemplates` (pure static, dependency-free HTML builder) was MOVED from `core.Infrastructure/Email/` to `core.Application/Identity/Settings/` because invite/activation handlers call it directly. `SettingsMailer` (the actual SMTP transport, behind `ISettingsMailer`) correctly stays in Infrastructure.
- `SettingsStore` (static settings reader) is already in Application and takes `ICoreDbContext`.
- Before reusing a legacy helper in a ported handler, confirm it isn't sitting in Infrastructure — if it is and it's dependency-free, relocate it to Application rather than adding an illegal project reference.
