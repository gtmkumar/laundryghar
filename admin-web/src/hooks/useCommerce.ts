import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  listPromotions,
  listCoupons,
  createCoupon,
  updateCoupon,
  deleteCoupon,
  listPackages,
  createPackage,
  updatePackage,
  deletePackage,
} from '@/api/commerce'
import type {
  CreateCouponPayload,
  UpdateCouponPayload,
  CreatePackagePayload,
  UpdatePackagePayload,
} from '@/types/api'

// ── Query key factory ─────────────────────────────────────────────────────────

export const commerceKeys = {
  promotions: () => ['commerce', 'promotions'] as const,
  coupons: () => ['commerce', 'coupons'] as const,
  packages: () => ['commerce', 'packages'] as const,
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

export function useCreateCoupon() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateCouponPayload) => createCoupon(payload),
    onSuccess: () => void qc.invalidateQueries({ queryKey: commerceKeys.coupons() }),
  })
}

export function useUpdateCoupon() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdateCouponPayload }) =>
      updateCoupon(id, payload),
    onSuccess: () => void qc.invalidateQueries({ queryKey: commerceKeys.coupons() }),
  })
}

export function useDeleteCoupon() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteCoupon(id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: commerceKeys.coupons() }),
  })
}

// ── Packages ──────────────────────────────────────────────────────────────────

export function usePackages() {
  return useQuery({
    queryKey: commerceKeys.packages(),
    queryFn: () => listPackages({ pageSize: 100 }),
    staleTime: 60_000,
  })
}

export function useCreatePackage() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreatePackagePayload) => createPackage(payload),
    onSuccess: () => void qc.invalidateQueries({ queryKey: commerceKeys.packages() }),
  })
}

export function useUpdatePackage() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdatePackagePayload }) =>
      updatePackage(id, payload),
    onSuccess: () => void qc.invalidateQueries({ queryKey: commerceKeys.packages() }),
  })
}

export function useDeletePackage() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deletePackage(id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: commerceKeys.packages() }),
  })
}
