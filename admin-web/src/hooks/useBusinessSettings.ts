import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getBusinessSettings,
  upsertBusinessSetting,
  clearBusinessSetting,
} from '@/api/businessSettings'
import type {
  BusinessSettingsQuery,
  UpsertSettingPayload,
  ClearSettingQuery,
} from '@/types/api'
import { useEffectiveBrandId } from './useBrandContext'

const ROOT = 'businessSettings'

/**
 * Lists the raw rows + resolved effective values for one category at the given
 * scope. Keyed by brand as well so a platform admin switching brands re-fetches.
 * `enabled` lets the caller hold the query until a scope selection is complete
 * (e.g. a franchise/store id has actually been picked).
 */
export function useBusinessSettings(query: BusinessSettingsQuery, enabled = true) {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: [ROOT, brandId, query.category, query.franchiseId ?? null, query.storeId ?? null],
    queryFn: () => getBusinessSettings(query),
    enabled: enabled && !!brandId,
  })
}

export function useUpsertBusinessSetting() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: UpsertSettingPayload) => upsertBusinessSetting(payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: [ROOT] }),
  })
}

export function useClearBusinessSetting() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (query: ClearSettingQuery) => clearBusinessSetting(query),
    onSuccess: () => qc.invalidateQueries({ queryKey: [ROOT] }),
  })
}
