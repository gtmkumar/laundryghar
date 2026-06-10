---
name: task26-test-baseline
description: Task #26 automated test baseline — mobile Jest, backend probes, smoke.sh — all gates green 2026-06-10
metadata:
  type: project
---

Task #26 test baseline completed 2026-06-10. All gates passed.

**Why:** Establish an automated test baseline before further feature work; lock down security regressions caught in earlier reviews.

**How to apply:** Use these counts and boundaries when assessing coverage for future PRs.

## Test counts

### Backend (xUnit, all green)
- Commerce.Tests: 44 (was 27; +17 new via prior agents — no gap in idempotency key, refund cap, CHECK literals)
- Identity.Tests: 54 (was 27; +27 new — OtpLockoutWindowTests added by this task: +20 window-cutoff tests)
- Orders.Tests: 141 (was 69; +72 new — PriceResolverScopeChainTests added by this task: +12 scope-chain tests)
- Finance.Tests: 41 | Logistics.Tests: 7 | Engagement.Tests: 40 | Worker.Tests: 119 | ServiceDefaults.Tests: 82
- Grand total: 528 (was ~455 before this task's additions)

### Mobile Jest (both green, zero pre-existing)
- customer-mobile: 115 tests across 5 suites (versionGate, format, cartStore, bookingStore, apiClient)
- rider-mobile: 63 tests across 4 suites (versionGate, offlineQueueStore, taskOverrideStore, apiClient)

### Smoke
- scripts/smoke.sh: 22 probes — 9 health checks + Worker pgrep + AdminLogin + 11 read probes
- Exits 0 on live stack as of 2026-06-10

## Key decisions
- jest-expo pinned to ~52.0.0 (not ~56) to match expo ~52.0.0; jest pinned to ^29
- "jest" added to tsconfig.json types array in both apps so mock/test files type-check
- Screen/navigation tests explicitly out of scope (expo-router native deps make them brittle)
- PriceResolverScopeChainTests mirrors LINQ ordering logic in-memory (no testcontainers)
- Royalty invoices route is /royalty-invoices (not /royalty/invoices) — discovered during smoke run

## Coverage gaps (explicitly deferred)
- Full PriceResolver.ResolveAsync with live DB (requires testcontainers)
- Rider-session live E2E
- Mobile component tests (OtpInput, Badge) — excluded due to nativewind className rendering complexity
- Screen/navigation tests — Maestro or Detox is the right tool
