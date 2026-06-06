/**
 * TanStack Query hooks for the Commerce service (customer-facing).
 * Mirrors the pattern in useOrders.ts / useCatalog.ts.
 */
import { useQuery } from '@tanstack/react-query';
import { getCoupons } from '@/api/commerce';

// ---------------------------------------------------------------------------
// Query keys
// ---------------------------------------------------------------------------

export const commerceKeys = {
  coupons: ['commerce', 'coupons'] as const,
} as const;

// ---------------------------------------------------------------------------
// useCoupons — GET /api/v1/customer/coupons
// Requires a valid customer Bearer token (attached automatically by client.ts).
// ---------------------------------------------------------------------------

export function useCoupons() {
  return useQuery({
    queryKey: commerceKeys.coupons,
    queryFn:  getCoupons,
    staleTime: 2 * 60 * 1_000, // coupons change more often than CMS content
  });
}
