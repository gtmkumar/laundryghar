# Laundry Ghar — Context Management

> How the agent team stays inside the context window and keeps each agent sharp. A 92-table schema plus 11 agents will blow a 1M-token window in a couple of features if handed around naively. This file is the discipline that prevents that.

**Core principle:** an agent should hold _only_ what it needs to do its current job — its own community's SQL, the foundation interfaces it consumes, and its operating manual. Not the whole project.

---

## The budget problem

A 1M-token context sounds infinite until you add it up:

```
Full schema (92 tables DDL)            ~45K tokens
PRODUCTION_SPEC.md                     ~60K tokens
All 11 agent manuals                   ~40K tokens
All 9 ADRs                             ~12K tokens
A community's existing code            ~80K tokens
Conversation history of a long build   grows unbounded
```

Hand an agent _all_ of that and you've spent 250K+ before it writes a line. Do it across a multi-feature session and you hit the wall mid-build, lose state, and the agent starts hallucinating tables that don't exist.

---

## Rules

### 1. Scope the schema per agent

Never give an agent the full `database/README.md`. Give it:

- the **one** `0N_*.sql` file it owns, and
- a short **interface contract** listing only the foundation tables/columns it reads (e.g., orders-engineer gets `customers(id, brand_id)`, `stores(id, brand_id)`, `services(id)`, `items(id)` — not the catalog's internal pricing tables).

This keeps schema context per agent under ~10K tokens instead of 45K.

### 2. Communities are context boundaries, not just code boundaries

The same decoupling that lets agents run in parallel (ADR-aligned: communities share only the tenant anchor) means an agent never needs another community's internals in context. If you find yourself wanting to paste the finance tables into the orders agent, the design is wrong — they should communicate via outbox events, and the event payload is the only contract that crosses.

### 3. Read the spec by section, not whole

`PRODUCTION_SPEC.md` is large. Agents read the **one section** relevant to their community plus the cross-cutting concerns section. The orchestrator extracts the relevant slice when spawning.

### 4. ADRs are reference, not preload

Don't preload all 9 ADRs into every agent. Load an ADR only when the agent is about to make a decision it governs (e.g., load ADR-004 when writing a migration for a partitioned table). The orchestrator points the agent at the specific ADR.

### 5. Summarize, then drop, conversation history

Long builds accumulate history. At each wave boundary the orchestrator writes a **checkpoint summary** (what was built, key decisions, open issues) and starts the next wave fresh from that summary rather than carrying raw history. The summary is ~2K tokens; the raw history it replaces is tens of thousands.

### 6. Canonical SQL stays on disk, not in chat

Agents `view` the SQL file when they need it and let it leave context afterward. They do not keep the full DDL pinned in conversation. The file on disk is the memory; the context window is the working set.

---

## Per-agent context budget (target)

```
Agent operating manual (agents/X.md)        ~3K tokens
Owned SQL file (0N_*.sql)                    ~6–10K tokens
Foundation interface contract                ~2K tokens
Relevant PRODUCTION_SPEC section             ~6K tokens
1–2 relevant ADRs (only when deciding)       ~2K tokens
Working code + current task                  ~40K tokens
─────────────────────────────────────────
Typical working set                          ~60–65K tokens
```

That leaves the bulk of the window for actual reasoning and code generation, and means an agent can run several features before needing a checkpoint reset.

---

## Checkpoint summary template

The orchestrator writes one of these at each wave boundary (and any time an agent's context exceeds ~60% of budget):

```markdown
## Checkpoint — Wave N, <community>

- Built: <files/endpoints/migrations created>
- Schema touched: <tables>
- Decisions: <new ADRs or deviations>
- Outbox events emitted: <event types>
- Open issues: <anything deferred>
- Next: <what the next agent/wave needs to know>
```

Start the next unit of work from this summary, not from raw history.

---

## Relationship to AGENT_TEAM.md

`AGENT_TEAM.md` answers **who does what when**. This file answers **how memory works**. They are complementary, not overlapping: dispatch vs. budget. When in doubt — dispatch questions → `AGENT_TEAM.md`; "my context is filling up" → here.
