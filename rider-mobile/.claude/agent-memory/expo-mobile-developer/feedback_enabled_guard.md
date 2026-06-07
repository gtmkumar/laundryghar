---
name: enabled-guard-on-auth-queries
description: All TanStack Query hooks that call authenticated endpoints must have enabled: !!accessToken to prevent pre-hydration 401 churn
metadata:
  type: feedback
---

Add `enabled: !!accessToken` to every `useQuery` that calls an authenticated endpoint. Without it, queries fire on mount before SecureStore hydration completes, the request 401s, React Query retries with backoff (`retry: 2` × `timeout: 15_000` = up to 45 s), and `isLoading` never settles — leaving the screen stuck on `<ScreenLoader />` indefinitely.

**Why:** Fixed in rider-mobile `src/hooks/useRider.ts` — `useMyRiderProfile` and `useTodaysAssignments` were firing before token hydration. Same bug class fixed on pos-web (per user note).

**How to apply:** In any `useQuery` hook that reads from an authenticated API client, read `const accessToken = useAuthStore(s => s.accessToken)` and pass `enabled: !!accessToken`. The store selector is safe to call at hook level (Zustand).
