import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  createGarment,
  createProcessLog,
  createStockReconciliation,
  getGarmentByTag,
  getWarehouseBoard,
  listStockReconciliations,
} from '@/api/warehouse'
import type { CreateGarmentRequest, CreateProcessLogRequest } from '@/types/api'
import { useEffectiveBrandId } from './useBrandContext'

/**
 * Live warehouse kanban — refetches every 20s. Gated on the effective brand id
 * so the request always carries brand context (the board renders outside the
 * AppShell, where platform_admin's brand is otherwise auto-selected). The page
 * also exposes `refetch` for a manual refresh button (R3-AW-5).
 */
export function useWarehouseBoard() {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: ['warehouse', 'board', brandId],
    queryFn: getWarehouseBoard,
    enabled: !!brandId,
    refetchInterval: 20_000,
  })
}

/**
 * Lookup a garment by its scanner-gun tag code. Used by the Scan In modal.
 * Only fires when tagCode is non-empty.
 */
export function useGarmentByTag(tagCode: string) {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: ['warehouse', 'garment-by-tag', brandId, tagCode],
    queryFn: () => getGarmentByTag(tagCode),
    enabled: !!brandId && tagCode.trim().length > 0,
    retry: false,      // 404 is a "not found" UX signal, not a transient failure
    staleTime: 10_000, // short TTL — tags are assigned once but we want freshness
  })
}

/**
 * Post a process-log (scan-in) event. Invalidates the board on success so the
 * card moves columns without a manual refresh.
 */
export function useCreateProcessLog() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: CreateProcessLogRequest) => createProcessLog(req),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['warehouse', 'board'] })
    },
  })
}

/**
 * List stock reconciliations for the active brand/warehouse.
 */
export function useStockReconciliations(page = 1, status?: string) {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: ['warehouse', 'recons', brandId, page, status],
    queryFn: () => listStockReconciliations(page, 20, status),
    enabled: !!brandId,
  })
}

/**
 * Create a new stock reconciliation session. Invalidates the recon list on success.
 */
export function useCreateStockReconciliation() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: {
      warehouseId: string | null
      storeId: string | null
      reconDate: string
      reconType: string
    }) => createStockReconciliation(req),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['warehouse', 'recons'] })
    },
  })
}

/**
 * Register a garment manually. Invalidates the board on success.
 */
export function useCreateGarment() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: CreateGarmentRequest) => createGarment(req),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['warehouse', 'board'] })
    },
  })
}
