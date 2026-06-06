import { useQuery } from '@tanstack/react-query'
import { listPromotions, listCoupons } from '@/api/commerce'

// ── Query key factory ─────────────────────────────────────────────────────────

export const commerceKeys = {
  promotions: () => ['commerce', 'promotions'] as const,
  coupons: () => ['commerce', 'coupons'] as const,
}

// ── Promotions ────────────────────────────────────────────────────────────────

export function usePromotions() {
  return useQuery({
    queryKey: commerceKeys.promotions(),
    queryFn: () => listPromotions({ pageSize: 100 }),
    staleTime: 60_000,
  })
}

// ── Coupons ───────────────────────────────────────────────────────────────────

export function useCoupons() {
  return useQuery({
    queryKey: commerceKeys.coupons(),
    queryFn: () => listCoupons({ pageSize: 100 }),
    staleTime: 60_000,
  })
}
