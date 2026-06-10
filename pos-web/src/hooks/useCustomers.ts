import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createAdminCustomer, getAdminCustomers } from '@/api/customers'
import { useBrandStore } from '@/stores/brandStore'
import { useAuthStore } from '@/stores/authStore'
import type { AdminCreateCustomerRequest, AdminCustomerListParams } from '@/types/api'

/**
 * Returns the effective brand id for the current session (JWT brand_id for
 * brand-scoped staff, else the platform_admin's manual selection). Customer
 * queries must not fire without a brand context or they 401.
 */
function useEffectiveBrandId(): string | null {
  const { accessToken } = useAuthStore()
  const { activeBrandId } = useBrandStore()

  if (accessToken) {
    try {
      const payload = JSON.parse(
        atob(accessToken.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')),
      ) as { brand_id?: string }
      if (payload.brand_id) return payload.brand_id
    } catch {
      // ignore parse errors
    }
  }
  return activeBrandId
}

export const customerKeys = {
  list: (params?: object) => ['customers', 'list', params] as const,
}

export function useCreateCustomer() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: AdminCreateCustomerRequest) => createAdminCustomer(req),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['customers', 'list'] })
    },
  })
}

/**
 * Searches customers by phone / name / code. Gated on a non-empty search term
 * AND brand context so we don't fire (or 401) on an empty counter session.
 */
export function useCustomerSearch(search: string, enabled = true) {
  const brandId = useEffectiveBrandId()
  const term = search.trim()
  const params: AdminCustomerListParams = { search: term, pageSize: 10 }
  return useQuery({
    queryKey: customerKeys.list(params),
    queryFn: () => getAdminCustomers(params),
    enabled: enabled && Boolean(brandId) && term.length >= 2,
    staleTime: 30_000,
  })
}
