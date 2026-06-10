# LaundryGhar — Testing Guide

## How to run each suite

### Backend (xUnit, .NET 10)

Run all 8 test projects at once from the solution root:

```bash
cd backend/laundryghar
dotnet test
```

Run a single project:

```bash
dotnet test laundryghar.Orders.Tests/laundryghar.Orders.Tests.csproj
dotnet test laundryghar.Commerce.Tests/laundryghar.Commerce.Tests.csproj
dotnet test laundryghar.Identity.Tests/laundryghar.Identity.Tests.csproj
```

No live database is required — all tests are pure in-memory.

### Mobile — customer-mobile (Jest, Expo SDK 52)

```bash
cd customer-mobile
npm test
```

Tests live in `src/__tests__/`. Mocks live in `src/__mocks__/`.

### Mobile — rider-mobile (Jest, Expo SDK 52)

```bash
cd rider-mobile
npm test
```

Tests live in `src/__tests__/`. Mocks live in `src/__mocks__/`.

### TypeScript type-check (both apps)

```bash
cd customer-mobile && npx tsc --noEmit
cd rider-mobile    && npx tsc --noEmit
```

Both apps include `"jest"` in `tsconfig.json` types so test and mock files
type-check without errors.

### Live-stack smoke test

Requires the full Aspire stack running (`dotnet run --project backend/laundryghar/laundryghar.AppHost`).

```bash
bash scripts/smoke.sh
```

Override the host if the stack is not on localhost:

```bash
BASE_HOST=192.168.1.x bash scripts/smoke.sh
```

The script exits 0 (SMOKE: PASSED) or non-zero (SMOKE: FAILED) and prints
a per-probe PASS/FAIL table. It performs no mutations — all probes are reads
or the single admin login.

---

## Coverage philosophy

### Mobile — logic-first

The mobile test baseline targets **pure TypeScript logic** only. Native
modules (`expo-location`, `expo-task-manager`, `react-native-reanimated`,
etc.) are excluded because they require a native build environment, and
mocking them produces tests that verify mock behaviour rather than real
behaviour.

Covered:

- `src/lib/versionGate.ts` — `semverGt` and `evaluateVersionGate`
  (both customer-mobile and rider-mobile)
- `src/lib/format.ts` — `rupees`, `formatDate`, `formatDateTime`, `greeting`
- `src/store/cartStore.ts` — add/update/remove items, count, subtotal, clear,
  selector stability regression
- `src/store/bookingStore.ts` — address/slot/express/paymentMethod/confirmed
  setters, reset regression
- `src/store/offlineQueueStore.ts` (rider) — hydrate, enqueue, dequeue,
  deduplication, setFlushing; AsyncStorage mocked in-memory
- `src/store/taskOverrideStore.ts` (rider) — complete, reset, idempotency
- `src/api/client.ts` — `unwrapSingle`, `unwrapList`, `unwrapPaginated`,
  `ApiError` (both apps)

### Screen and navigation test boundary

Component tests are intentionally **out of scope** for this baseline except
where the component is cheap and has no native deps. Screen tests (anything
that renders a full expo-router screen or uses React Navigation) are
explicitly deferred because:

1. `expo-router` requires a native environment to resolve file-based routes.
2. Navigation state (`useLocalSearchParams`, `router.push`, etc.) mocks produce
   brittle tests that break on every router version bump.
3. The signal-to-noise ratio is low — screen renders test third-party library
   glue, not application logic.

When screen tests are needed (e.g., E2E accessibility or visual regression),
use Maestro or Detox against a real device/simulator rather than Jest.

### Backend — integration probes

Backend tests are **pure in-memory** unit/integration probes. They pin:

- DB CHECK constraint literals (refund status, method, type)
- Cumulative payment cap and idempotency key determinism
- Refund cap arithmetic
- OTP HMAC/salt hashing, lockout threshold, and window time-cutoff math
- Price resolver scope-chain priority (store > franchise > brand)
- Validator rules (order status enum, cancel reason length, delivery
  assignment status, slot capacity, pickup reason)
- Invoice FY calculation and tax arithmetic
- Coupon math (percent, flat, cap)
- Ops-queue date-window classification
- RazorPay HMAC signature verification (no HTTP calls)
- Worker notification status mapping

No EF Core in-memory DB or testcontainers are used. Tests that require a
live DB (e.g., full PriceResolver.ResolveAsync) are deferred to the E2E
live-test plan.

### Smoke — liveness

`scripts/smoke.sh` verifies that all 9 services are alive and respond to
authenticated reads. It is not a correctness test — it is a
**go/no-go gate** before deploying or handing off to QA.
