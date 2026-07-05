---
name: project-setup
description: Set up a production-grade project scaffold (TypeScript, Python, or C#/.NET) with strict configs, pre-commit quality gates, skills/ reference docs, CLAUDE.md, and WELCOME.md. Use when the user asks to set up, scaffold, or bootstrap a new project or repo, or invokes /project-setup. Runs an interactive questionnaire first; supports express (solo, ~15 min) and full (team/production, ~45 min) modes.
---

# Project Setup — Top 1% Engineering Standards

This skill sets up a production-grade project scaffold tailored to the user's stack and team.
It works in two modes — **express** (solo/early-stage, ~15 min) and **full** (team/production, ~45 min).

Engineering-standards reference content (Part II) lives in
[references/engineering-standards.md](references/engineering-standards.md) — read it when
generating the skills/ files in Step 11, not before.

# AGENT BEHAVIOR RULES

Before writing a single file, you must:

1. Run the stack detection commands (Step 1) — silently
2. Ask ALL questions in the questionnaire (Step 2) — wait for answers before proceeding
3. Confirm the chosen mode and plan with the user
4. Execute setup steps, skipping any that don't apply to the confirmed stack

Never:
- Create files until the questionnaire is complete
- Install tools without confirming the user wants them
- Generate language configs for a language not in the confirmed stack
- Skip the questionnaire because the stack "seems obvious"
- Report "setup complete" before the final validation checklist passes

---

# PART I — INTERACTIVE SETUP

## Step 1: Detect Existing Stack

Run silently — do not report output, just note what exists:

```bash
ls -la
cat package.json 2>/dev/null || true
cat pyproject.toml 2>/dev/null || true
ls *.sln *.slnx 2>/dev/null || true
find . -maxdepth 3 -name "*.csproj" -not -path "*/node_modules/*" 2>/dev/null | head -20
cat global.json 2>/dev/null || true
cat Directory.Build.props 2>/dev/null || true
```

Do not act on findings yet. The questionnaire determines what to build.

---

## Step 2: Questionnaire — Ask All Before Proceeding

Present these questions conversationally — do not dump all at once.
Group A first, wait for answers, then B, then C and D together. All answers are needed before Step 3.

### A. Project Identity

1. **What is this project?** One sentence: what it builds and who it's for.
2. **Primary language / framework?**
   - TypeScript / JavaScript — and if so, which framework: Next.js, Express, Node, React, Vue, Svelte, other?
   - Python — and if so: FastAPI, Django, Flask, scripts/data, other?
   - C# / .NET — and if so: ASP.NET Core Web API (Clean Architecture), Minimal API, Worker Service, Blazor, .NET Aspire distributed app, other?
   - Multiple languages (which ones)?
3. **Project type?**
   - Full-stack web app (frontend + API)
   - Backend API / microservice only
   - Frontend only (SPA or static site)
   - CLI tool
   - Library / SDK
   - Data pipeline / scripts
   - Mobile app backend

### B. Structure and Team

4. **Single service or multiple?**
   - Single repo / single service
   - Monorepo (multiple services, one repo)
   - Polyrepo (multiple services, separate repos — setting up this one only)
5. **Team size?**
   - Solo (just me)
   - Small (2–5 engineers)
   - Growing (6+ engineers)

### C. Infrastructure

6. **Deployment target?**
   - Vercel / Netlify
   - AWS (EC2, ECS, Lambda, etc.)
   - GCP / Azure
   - Railway / Render / Fly.io
   - Self-hosted / on-prem
   - Not decided yet
7. **Database?** (skip if no database)
   - PostgreSQL
   - MySQL / MariaDB
   - SQLite
   - MongoDB
   - Supabase (hosted Postgres)
   - No database / not decided yet
8. **Auth strategy?** (skip if no auth)
   - Self-built (JWT / sessions)
   - Clerk / Auth0 / Supabase Auth
   - No auth / not decided yet

### D. Engineering Preferences

9. **Testing philosophy?**
   - TDD (tests written first, always)
   - Test-after (code first, tests after)
   - Integration-first (mostly integration + e2e)
   - Minimal (tests where it makes sense)
10. **CI/CD?**
    - GitHub Actions
    - GitLab CI / CircleCI / other
    - None / not yet
11. **Setup mode?**
    - **Express** — lean scaffold, core files only. Best for: solo projects, early-stage, side projects.
    - **Full** — all 14 skills/ files, pre-commit hooks, graphify, agentation, AGENTS.md. Best for: team projects, production-bound work.

---

## Step 3: Confirm Plan Before Executing

Summarize back to the user:

```
Stack:     [languages + frameworks confirmed]
Type:      [project type]
Mode:      [express / full]
Services:  [single / monorepo / polyrepo]
Will create:
  - Directory structure
  - [list of config files for confirmed stack only]
  - skills/ files ([count] files)
  - CLAUDE.md, WELCOME.md
  - [full mode: AGENTS.md, COMMIT.md, pre-commit.sh, graphify, agentation if applicable]

Proceed?
```

Wait for confirmation before creating anything.

---

## Step 4: Directory Structure

### Single Service
```
project-root/
├── src/                    ← application source
├── skills/                 ← reference docs for agents and engineers
├── plan/                   ← research docs (always gitignored — never ships)
├── tests/
├── CLAUDE.md
├── WELCOME.md
├── AGENTS.md               ← full mode only
├── COMMIT.md               ← full mode only
└── pre-commit.sh
```

### Monorepo (multiple services)
```
project-root/               ← NOT a git repo
├── [frontend or app]/      ← git init here
├── [backend or api]/       ← git init here
├── [shared or packages]/   ← git init here (if applicable)
├── skills/                 ← shared reference docs
├── plan/                   ← gitignored
├── CLAUDE.md
├── AGENTS.md               ← full mode only
├── COMMIT.md               ← full mode only
├── WELCOME.md
└── pre-commit.sh
```

**Absolute rules:**
- `plan/` is always in `.gitignore`. Research never ships.
- In a monorepo, the project root is NOT a git repo. Each service is independently versioned.
- Each service git repo gets a pre-commit hook symlinked to `../../pre-commit.sh`.

---

## Step 5: .gitignore

```
# Research — never ships
plan/

# Secrets
.env
.env.*
!.env.example

# Dependencies
node_modules/
.venv/
vendor/

# Build artifacts
dist/
build/
.next/
out/

# .NET
bin/
obj/
*.user
TestResults/

# Python
__pycache__/
*.py[cod]
.mypy_cache/
.ruff_cache/
.pytest_cache/
*.egg-info/

# Coverage
coverage/
.coverage
htmlcov/

# Logs
*.log

# Graphify
graph.json
graph.html
graphify-out/
GRAPH_REPORT.md
```

---

## Step 6: Language-Specific Config

Generate ONLY the configs for languages confirmed in the questionnaire.

---

### TypeScript / JavaScript Projects

**tsconfig.json**
```json
{
  "compilerOptions": {
    "strict": true,
    "noUncheckedIndexedAccess": true,
    "noImplicitReturns": true,
    "noFallthroughCasesInSwitch": true,
    "exactOptionalPropertyTypes": true,
    "forceConsistentCasingInFileNames": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "allowJs": false
  }
}
```

`noUncheckedIndexedAccess` is non-negotiable — `arr[0]` returns `T | undefined`, not `T`.

**eslint.config.mjs**
```js
{
  rules: {
    "@typescript-eslint/no-explicit-any": "error",
    "@typescript-eslint/no-unsafe-assignment": "error",
    "@typescript-eslint/no-unsafe-member-access": "error",
    "@typescript-eslint/no-unsafe-return": "error",
    "@typescript-eslint/no-non-null-assertion": "error",
    "eqeqeq": ["error", "always"],
    "no-cond-assign": "error",
    "@typescript-eslint/strict-boolean-expressions": "error",
    "no-console": ["error", { "allow": ["warn", "error"] }],
    "@typescript-eslint/no-unused-vars": ["error", {
      "argsIgnorePattern": "^_",
      "varsIgnorePattern": "^_",
      "caughtErrorsIgnorePattern": "^_",
    }],
  }
}
```

**Hook naming rule:** Only name a function `useX` if it calls React hooks. Plain utilities that don't call hooks must use a verb prefix (`apply`, `handle`, `build`, `get`). Naming a non-hook `useX` triggers `react-hooks/rules-of-hooks` the moment it's called inside a callback.

**`eslint-disable` policy:** Every suppression must have a `//` explanation comment on the line immediately above it. Pre-commit enforces this.

---

### Python Projects

**pyproject.toml**
```toml
[tool.mypy]
python_version = "3.12"
strict = true
disallow_any_explicit = true
warn_unreachable = true
warn_unused_ignores = true

[tool.ruff]
target-version = "py312"
line-length = 100

[tool.ruff.lint]
select = ["E", "W", "F", "I", "B", "UP", "S", "SIM", "RUF", "ANN", "N", "PT"]
ignore = ["ANN101", "ANN102", "S101"]
```

---

### C# / .NET Projects

**global.json** — pin the SDK. No "works on my machine" builds.
```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

**Directory.Build.props** (solution root — applies to every project automatically)
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-all</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

`<Nullable>enable</Nullable>` + `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` is non-negotiable — a possible-null dereference is a compile error, not a 2am `NullReferenceException`.

**.editorconfig** (key severity rules — full file generated at setup)
```ini
[*.cs]
dotnet_diagnostic.CA2007.severity = none          # ConfigureAwait — app code, not a library
dotnet_diagnostic.CA1062.severity = error         # validate public args
dotnet_diagnostic.CA1305.severity = error         # IFormatProvider — culture bugs
dotnet_diagnostic.CA2016.severity = error         # forward CancellationToken
dotnet_diagnostic.CS8618.severity = error         # non-nullable uninitialized
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_namespace_declarations = file_scoped:error
dotnet_style_require_accessibility_modifiers = always:error
```

**Rules:**
- `async void` is banned everywhere except event handlers.
- `.Result` / `.Wait()` / `GetAwaiter().GetResult()` on async code is banned — deadlock + thread-pool starvation.
- Every async method that does I/O accepts and forwards a `CancellationToken`.
- No `dynamic` in domain or application code.
- Custom CQRS dispatch (`ICommandHandler<,>` / `IQueryHandler<,>`) — no MediatR or any mediator library.

---

## Step 7: Design Token System (Frontend Projects Only)

Skip this step entirely if the project has no frontend UI.

All colors, fonts, and spacing live in ONE file: `src/design/tokens.ts`.

```typescript
// The only place design values are defined. Change here → reflects everywhere.
export const Colors = { /* hex values */ } as const;
export const Typography = { fontFamily, fontSize, lineHeight, fontWeight } as const;
export const Spacing = { /* 4px base unit, named scale */ } as const;
export const Radius = { sm, md, lg, xl, full } as const;
export const Shadow = { sm, md, lg, glow } as const;
```

Rules:
- Never hardcode a hex value in a component
- CSS custom properties generated from this file — TypeScript and CSS stay in sync
- Font size change = one line in `tokens.ts`

For Tailwind 4 projects: define in `@theme` in `globals.css`, mirror the token names in a TypeScript `TOKENS` constant using `satisfies`.

---

## Step 8: Constants Architecture & Branded Types (No Free-Floating Strings)

### TypeScript
```typescript
// src/constants/[domain].constants.ts
export const UserStatus = {
  ACTIVE:    'active',
  SUSPENDED: 'suspended',
  DELETED:   'deleted',
} as const;
export type UserStatus = (typeof UserStatus)[keyof typeof UserStatus];

// Barrel export — src/constants/index.ts
export * from './user-status.constants';
export * from './api-paths.constants';
export * from './error-messages.constants';
```

### Python
```python
from enum import StrEnum
from typing import Final

class UserStatus(StrEnum):
    ACTIVE    = "active"
    SUSPENDED = "suspended"
    DELETED   = "deleted"

MAX_ITEMS_PER_PAGE: Final[int] = 100
```

### C#
```csharp
// Domain/Constants/UserStatus.cs — smart enum, DB-friendly string values
public sealed record UserStatus
{
    public static readonly UserStatus Active    = new("active");
    public static readonly UserStatus Suspended = new("suspended");
    public static readonly UserStatus Deleted   = new("deleted");

    public string Value { get; }
    private UserStatus(string value) => Value = value;

    public static UserStatus From(string value) => value switch
    {
        "active"    => Active,
        "suspended" => Suspended,
        "deleted"   => Deleted,
        _ => throw new ArgumentException($"Invalid UserStatus: {value}"),
    };
}

// Domain/Constants/Limits.cs
public static class Limits
{
    public const int MaxItemsPerPage = 100;
}
```

**Rule:** Before writing any feature, identify the string/number constants it needs. Add them to the constants file first. No literals in domain or application code.

### Branded / Opaque Types (No Free-Floating Strings)

Plain `string` (and `number`) types are the source of a whole class of bugs: a `UserId` gets passed where an `OrgId` was expected, an unvalidated raw input flows into a function that assumes it's been checked, and the compiler says nothing. Every domain identifier and meaningful value gets its own **opaque brand type** so the type system — not code review — guarantees the right value reaches the right place.

**Rule:** No domain value travels as a bare `string`/`number`. If a value has meaning (an id, a token, an email, a slug, a path, a currency amount), give it a brand. A branded value can only be produced by its constructor/validator, so once you hold one, its provenance is proven by the type. Constructors live next to the type; they are the single chokepoint where a raw string becomes a branded value.

#### TypeScript
```typescript
// src/types/brand.ts — one tiny helper, reused everywhere
declare const __brand: unique symbol;
export type Brand<T, B extends string> = T & { readonly [__brand]: B };

// src/domain/user/user.types.ts
export type UserId = Brand<string, 'UserId'>;
export type OrgId  = Brand<string, 'OrgId'>;
export type Email  = Brand<string, 'Email'>;

// Constructors are the ONLY way to mint a branded value — validate at the boundary.
export const UserId = (raw: string): UserId => {
  if (!/^usr_[a-z0-9]+$/.test(raw)) throw new Error(`Invalid UserId: ${raw}`);
  return raw as UserId;
};
export const Email = (raw: string): Email => {
  if (!raw.includes('@')) throw new Error(`Invalid Email: ${raw}`);
  return raw.toLowerCase() as Email;
};

// Now this is a COMPILE error, not a 2am incident:
function getUser(id: UserId) { /* ... */ }
declare const orgId: OrgId;
getUser(orgId);           // ✗ Argument of type 'OrgId' is not assignable to 'UserId'
getUser('usr_123');       // ✗ string is not assignable to 'UserId'
getUser(UserId('usr_123'));  // ✓ provenance proven by the type
```

#### Python
```python
from typing import NewType
import re

UserId = NewType("UserId", str)
OrgId  = NewType("OrgId", str)
Email  = NewType("Email", str)

# Smart constructors — the only sanctioned way to build a branded value.
def make_user_id(raw: str) -> UserId:
    if not re.fullmatch(r"usr_[a-z0-9]+", raw):
        raise ValueError(f"Invalid UserId: {raw}")
    return UserId(raw)

# mypy flags getUser(org_id) where getUser expects UserId.
# (For runtime-validated brands, Pydantic models or Annotated types are the heavier alternative.)
```

#### C#
```csharp
// Strongly-typed ID — readonly record struct: zero heap allocation, fully distinct at compile time.
public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.CreateVersion7());
    public static UserId Parse(string raw) =>
        Guid.TryParse(raw, out var g) ? new(g)
        : throw new ArgumentException($"Invalid UserId: {raw}");
    public override string ToString() => Value.ToString();
}

public readonly record struct OrgId(Guid Value);

// Task<User> GetUserAsync(UserId id, CancellationToken ct)
// — passing an OrgId or a bare Guid is a compile error.
```

**EF Core value converter — the ONLY place the brand unwraps to a primitive:**
```csharp
// Infrastructure/Persistence/Configurations/UserConfiguration.cs
builder.Property(u => u.Id)
    .HasConversion(id => id.Value, value => new UserId(value));
```

Register a converter per branded type in `ConfigureConventions` so no `Guid`/`string` ever leaks into domain signatures. Same pattern for API boundaries: a `JsonConverter<UserId>` (or minimal-API `TryParse` support) so DTOs also carry the brand.

**Agent rule:** When you introduce a new identifier or meaningful value, define its brand type and constructor first, then thread the branded type through every signature. Never widen back to `string`/`number` to "make it pass" — fix the type, or add a validating constructor at the boundary where the raw value enters.

---

## Step 9: External Tools (Full Mode)

### Graphify — Orient Before You Explore
```bash
pip install graphifyy
graphify install
graphify claude install  # registers PreToolUse hook in Claude Code
```

Run on each service after creation:
```bash
/graphify src/
/graphify backend/
```

**Agent rule:** Read `GRAPH_REPORT.md` before exploring any unfamiliar module. Never grep blindly when the knowledge graph answers the question in one read.

### Iris — Automated Manual Testing (React / Next.js / any React-based framework — MANDATORY)

> Source: https://github.com/syrin-labs/iris

Iris automates manual UI testing and closes the feedback loop between code changes and visual/behavioral verification. It is **mandatory** for any React-based project — install it before writing the first component.

```bash
cd app && npm install @syrin/iris -D
```

Follow the setup instructions at https://github.com/syrin-labs/iris for framework-specific configuration (Next.js app router, Vite, CRA, etc.).

**Why mandatory:** Manual testing is the last mile of every UI feature. Without Iris, feedback loops are slow — you change code, manually click through flows, and miss regressions in adjacent paths. Iris automates this so every agent and engineer gets instant, repeatable feedback on real browser behavior, not just type-check output.

**Agent rule:** Before marking any UI task complete, run the relevant Iris test. "Type-checks pass" is not the same as "the feature works."

### Agentation — Visual Feedback (React / Next.js only)
```bash
cd app && npm install agentation agentation-mcp -D
npx add-mcp "npx -y agentation-mcp server"
```

Add to root layout (dev only):
```tsx
{process.env.NODE_ENV === 'development' && <Agentation />}
```

### Refero Design Reference (Frontend projects)
```bash
npx skills add https://github.com/referodesign/refero_skill
```

Search: `/refero [niche]` e.g. `/refero "SaaS dashboard"`. Extract: color palette, typography scale, spacing rhythm, shadow system. Put all tokens in `skills/design.md` — the single source of truth.

---

## Step 10: pre-commit.sh Quality Gate

Generate only the sections for languages in the confirmed stack.
Order of operations — always this order, never skip:

1. **Safety** — no secrets staged, no `plan/` staged, no `any` (TS), no `console.log` (TS), no `dynamic` (C#), no `.Result`/`.Wait()` (C#), no files over 500 lines
2. **Python** (if in stack) — ruff format → ruff lint → mypy
3. **TypeScript** (if in stack) — prettier → eslint → tsc --noEmit
4. **C#** (if in stack) — `dotnet format --verify-no-changes` → `dotnet build -warnaserror --nologo` (analyzers run in build)
5. **Tests** — fast unit tests only. Integration tests belong in CI only.
6. **Summary** — colored pass/fail count, exit 1 if any failure

Install as git hook in each service:
```bash
ln -sf ../../pre-commit.sh app/.git/hooks/pre-commit
ln -sf ../../pre-commit.sh backend/.git/hooks/pre-commit
chmod +x pre-commit.sh
```

---

## Step 11: Generate skills/ Reference Files

> This step is what separates this skill from a setup script.
> These files are what engineers and agents open every day.
> Generate them with real engineering depth — adapt all examples to the actual project stack.
> Read [references/engineering-standards.md](references/engineering-standards.md) NOW (Part II).
> Content comes from that file AND your own knowledge of best practices for the detected stack.

### Full Mode — All 14 Files

| File                      | Content source                   | Open when                             |
|---------------------------|----------------------------------|---------------------------------------|
| `skills/design.md`        | Part II (design tokens) + refero | Building any UI component             |
| `skills/typescript.md`    | Part II.11 + TS patterns         | Any TypeScript question or pattern    |
| `skills/python.md`        | Part II.11 + Python patterns     | Any Python question or pattern        |
| `skills/csharp.md`        | Part II.11 + C#/.NET patterns    | Any C# / .NET question or pattern     |
| `skills/testing.md`       | Part II.7 + TDD workflow         | Before writing any feature code       |
| `skills/conventions.md`   | Part II (constants + branded types) | Before naming anything             |
| `skills/agents.md`        | Part II (agent patterns)         | Multi-agent task or orchestration     |
| `skills/architecture.md`  | Part II.9 + service contracts    | New service or API contract           |
| `skills/database.md`      | Part II.12 (full database guide) | Before any table, query, or schema    |
| `skills/security.md`      | Part II.4 + security checklist   | Any endpoint touching user data       |
| `skills/performance.md`   | Part II.3 + frontend perf        | Anything slow or memory-hungry        |
| `skills/observability.md` | Part II.6 + alerting + tracing   | Logs, metrics, or circuit breakers    |
| `skills/api-design.md`    | Part II (API section) + REST     | Any new API endpoint                  |
| `skills/cicd.md`          | Part II (infra) + Docker + deploy| CI pipeline, Docker, deployment       |

Mark inapplicable files (e.g., `skills/design.md` for a pure API project) with "N/A for this stack" at the top rather than omitting them.

### Express Mode — 4 Core Files

Generate only:
- `skills/conventions.md` — always needed
- `skills/testing.md` — always needed
- `skills/architecture.md` — needed from day 1
- One language-specific file (`skills/typescript.md` OR `skills/python.md` OR `skills/csharp.md`)

---

## Step 12: Generate CLAUDE.md

Must include:

1. **Project overview** — name, one-paragraph description, and stack (from questionnaire)
2. **Directory structure** with annotations
3. **Service boundary rules** — who owns what; which services handle which concerns
4. **Non-negotiables** — pulled from Part II (references/engineering-standards.md), adapted to the confirmed stack:
   - Always `===` not `==` (TS) / always `==` not `is` for values (Python) / no `==` on floating-point, use `Equals` semantics intentionally (C#)
   - No `any`, no non-null assertions (TS) / `mypy strict` (Python) / `Nullable enable`, no `dynamic`, no `async void`, no `.Result`/`.Wait()` (C#)
   - No free-floating strings — constants first, and every domain value carries an opaque brand type (no bare `string`/`number` for ids, tokens, emails, etc.)
   - Tests before (or alongside) code
   - 500-line file cap
   - No `SELECT *`, no unbounded queries (if database in stack)
5. **Naming conventions table** — adapted to language(s) in stack
6. **Pre/post coding checklist:**
   - Scan for reuse before writing
   - Write failing test first (if TDD mode confirmed)
   - Code to green
   - Refactor with tests passing
   - Check file length (500 line cap)
7. **Reference table** — links to all skills/ files created and when to open each

---

## Step 13 (Full Mode): AGENTS.md and COMMIT.md

### AGENTS.md
```markdown
# Agent Orchestration

## Tool Manifest
| Tool      | Use for                              | When NOT to use                  |
|-----------|--------------------------------------|----------------------------------|
| Graphify  | Orient before exploring a module     | Already know file location       |
| Iris      | Automated UI testing, feedback loop  | Non-UI / non-React changes       |
| Agentation| Visual verification of UI changes    | Non-UI changes                   |
| Bash      | Shell operations, git, build         | Reading/writing files            |
| Read      | Reading known file paths             | Broad codebase exploration       |
| Explore   | Finding files, grepping across repo  | Single known file                |

## Parallelization Rules
- Independent reads: run in parallel
- File writes that could conflict: sequential
- Test suite: parallel by test file
- Cross-service API calls: always async

## Context Handoff
When handing off between agents:
- Include: current task, files modified, tests status, blockers
- Always attach: relevant skills/ file path for the domain
```

### COMMIT.md
```markdown
# Pre-Commit Checklist

□ No secrets or .env files staged?
□ plan/ excluded from staging?
□ All linters pass?
□ Type checker passes with zero errors?
□ Any failing tests? If so, explain why in the commit message.
□ Any file over 500 lines? If so, refactor first.
□ eslint-disable comments have an explanation comment above them?
□ New constants added to constants/ before use in domain code?
□ New domain values (ids, tokens, emails…) given a brand type, not passed as bare string/number?
□ New DB columns: migration is backward-compatible with running app code?
```

---

## Step 14: Generate WELCOME.md

Create `WELCOME.md` at the project root. Must include:

1. Project name and one-line description
2. Stack summary (languages, frameworks, deployment, key choices from questionnaire)
3. Annotated directory structure
4. First 5 commands to run after cloning
5. Table: "File → When to open it" for all skills/ files created
6. Non-negotiables for this stack
7. Installed tools and how to invoke them
8. Feature build order (what to build first and why, given the project type)

This is the first file a new engineer or agent reads. Make it the orientation they need.

---

## Step 15: Git Setup and Final Validation

```bash
# Single service
git init
ln -sf ../pre-commit.sh .git/hooks/pre-commit
chmod +x pre-commit.sh

# Monorepo — run per service
cd frontend && git init && ln -sf ../../pre-commit.sh .git/hooks/pre-commit
cd ../backend && git init && ln -sf ../../pre-commit.sh .git/hooks/pre-commit
```

### Final Validation Checklist

Do NOT report "setup complete" until all applicable items pass.

**Always (both modes):**
```
□ .gitignore includes plan/, .env, all build artifacts, graphify outputs?
□ CLAUDE.md exists and references all skills/ files created?
□ WELCOME.md created with first-5-commands and skills table?
□ pre-commit.sh executable and runs without error on empty project?
□ Constants architecture set up with code examples for this stack?
□ .env.example created for each service with all required vars documented?
□ Git initialized and pre-commit hook linked?
```

**Full mode only:**
```
□ All 14 skills/ files created (or marked N/A for stack)?
□ AGENTS.md exists with tool manifest?
□ COMMIT.md created?
□ Design token file created? (frontend only)
□ eqeqeq: "error" and no-cond-assign: "error" in ESLint? (TS projects)
□ mypy strict = true and disallow_any_explicit = true? (Python projects)
□ Nullable enable + TreatWarningsAsErrors in Directory.Build.props? (C# projects)
□ Graphify installed and knowledge graph built for each service?
□ Iris installed and configured? (any React-based project — mandatory)
□ Agentation installed? (Next.js / React projects)
□ Refero design skill installed? (frontend projects)
```

---
