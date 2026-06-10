---
name: rider-ops-screens
description: Task #16 — rider-mobile ops screens (earnings, cash, directions, failure modal, offline queue) + backend self-endpoints
metadata:
  type: project
---

Rider ops screens shipped (Task #16). Both gates passed: tsc 0, dotnet build 0 errors.

**Backend (Logistics service — all new files, no edits to RiderCodCommands.cs/RiderDutyCommands.cs):**
- `Application/RiderSelf/RiderPayoutSelfQueries.cs` — `GetMyPayoutsQuery(UserId, BrandId, days)` → `RiderPayoutSummaryDto` (total, avgPerTask, breakdown per IST calendar day). Days validated 1–90 via FluentValidation. Groups completed legs by `CompletedAt` converted to IST offset.
- `Application/RiderCod/RiderCodSelfQueries.cs` — `GetMyCashSummaryQuery(UserId, BrandId)` → `RiderCashSummaryDto` (cashInHand = outstanding unsettled COD sum, lastSettlementAt, recentSettlements ≤10). Self-filtered via JWT userId → Rider.Id.
- `RiderSelfDtos.cs` extended: `RiderTaskStatusUpdateRequest` now has optional `Reason` and `Note` for `status=failed`.
- `RiderTaskQueries.cs` extended: `UpdateMyTaskStatusCommand` carries `FailureReason?/FailureNote?`; handler stamps `CancellationReason` + `Notes` on the DeliveryAssignment when status=failed (no DB patch — columns already exist).
- `LogisticsEndpoints.cs` added: `GET /api/v1/rider/payouts?days=30` and `GET /api/v1/rider/cash/summary` under `riderSelf` group (RiderOnly policy).

**No DB patches needed** — `delivery_assignments.cancellation_reason` and `notes` columns already exist.

**App (rider-mobile) — new files:**
- `src/api/earnings.ts` — `fetchMyPayouts(days)` + `fetchMyCashSummary()` wrappers
- `src/api/tasks.ts` — added `failTaskStatus(taskId, reason, note?)` for structured failure
- `src/hooks/useEarnings.ts` — `useMyPayouts(days)` + `useMyCashSummary()` React Query wrappers; keys: `['rider','payouts',days]` + `['rider','cash','summary']`
- `src/store/offlineQueueStore.ts` — Zustand + AsyncStorage queue; `enqueue/dequeue/hydrate/setFlushing`; key `lg_rider_offline_queue`
- `src/hooks/useOfflineQueueFlush.ts` — flushes on AppState→active + useFocusEffect; no netinfo dep (not installed)
- `app/(app)/earnings.tsx` — period-total header, 7d/30d toggle, per-day FlatList
- `app/(app)/cash.tsx` — amber cash-in-hand card (>0), last settlement row, settlement list

**App — edited files:**
- `app/(app)/_layout.tsx` — registered `earnings` + `cash` screens
- `app/(app)/home.tsx` — added Earnings + Cash quick-action row below tasks pill
- `app/(app)/profile.tsx` — added Earnings + Cash quick-link row above Log out
- `app/(app)/tasks/[id].tsx` — Directions button (Google Maps URL, lat/lng or address fallback); failure modal (reason picker + note TextInput); offline-queue banner; `useOfflineQueueFlush` via useFocusEffect
- `app/_layout.tsx` — hydrates offline queue on boot
- `src/types/api.ts` — added `RiderPayoutDayDto`, `RiderPayoutSummaryDto`, `RiderCashSettlementItemDto`, `RiderCashSummaryDto`
- `.expo/types/router.d.ts` — manually extended to include `/earnings` and `/cash` routes (auto-regenerated on next `expo start`)

**Why:** [[rider-mobile-v2]] ops screens were stub; payout and COD data already in DB from Rider Ops phases 3+4.

**Offline queue design:** No netinfo. Flush on AppState→active + useFocusEffect. PATCH status is idempotent server-side (same terminal status is a no-op in the handler's switch).
