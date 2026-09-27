import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { getPolicies, getDecisions, updatePolicy } from '@/api/policies'
import type { PolicyEditPayload } from '@/types/api'
import { useEffectiveBrandId } from './useBrandContext'
import { usePermissions } from './usePermissions'

/**
 * ABAC policy authoring + explain (A8).
 *
 * Both queries are keyed on the effective brand: a platform admin switching brands with the
 * X-Brand-Id override must not be served the previous brand's rows out of cache.
 */
export function usePolicies(params: { resourceType?: string; search?: string } = {}) {
  const brandId = useEffectiveBrandId()
  const { hasPermission } = usePermissions()

  return useQuery({
    queryKey: ['abac-policies', brandId, params.resourceType ?? null, params.search ?? null],
    queryFn: () => getPolicies(params),
    // Gated client-side too, so a user without the permission does not fire a request that can
    // only 403 — the server check is what actually enforces it.
    enabled: hasPermission('roles.list'),
    staleTime: 30_000,
  })
}

export function useDecisions(params: { userId?: string; deniesOnly?: boolean; limit?: number } = {}) {
  const brandId = useEffectiveBrandId()
  const { hasPermission } = usePermissions()

  return useQuery({
    queryKey: ['abac-decisions', brandId, params.userId ?? null, params.deniesOnly ?? true, params.limit ?? 100],
    queryFn: () => getDecisions(params),
    enabled: hasPermission('audit.read'),
    // The decision log is a live feed; a long stale window would make it look frozen while
    // somebody is watching a shadow window fill up.
    staleTime: 5_000,
    refetchInterval: 15_000,
  })
}

export function useUpdatePolicy() {
  const qc = useQueryClient()

  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: PolicyEditPayload }) =>
      updatePolicy(id, payload),
    // Not optimistic, deliberately. A policy edit can be REFUSED by the server — a brand may
    // tighten a platform policy but never loosen one (A8.3) — so flipping the switch locally would
    // show the operator a rule that is not in force. This is one of the cases the house optimistic
    // pattern explicitly leaves pessimistic.
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['abac-policies'] })
      void qc.invalidateQueries({ queryKey: ['abac-decisions'] })
    },
  })
}
