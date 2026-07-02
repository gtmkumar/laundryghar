import { catalogClient, unwrap } from './client'
import type {
  ApiResponse,
  SettingsListDto,
  SettingRow,
  BusinessSettingsQuery,
  UpsertSettingPayload,
  ClearSettingQuery,
} from '@/types/api'

// Business rules live on the operations host (same host as the catalog), so we
// reuse catalogClient — the '/ops'-prefixed instance — not the identity client.
const BASE = '/api/v1/admin/business-settings'

/**
 * Lists the raw override rows plus the resolved effective values for one
 * category at the given scope. Passing franchiseId/storeId narrows the `rows`
 * to that scope while `effective` reflects the full resolution chain.
 */
export async function getBusinessSettings(
  query: BusinessSettingsQuery,
): Promise<SettingsListDto> {
  const { data } = await catalogClient.get<ApiResponse<SettingsListDto>>(BASE, {
    params: {
      category: query.category,
      franchiseId: query.franchiseId,
      storeId: query.storeId,
    },
  })
  return unwrap(data)
}

/**
 * Upserts a setting at a writable scope. `value: null` clears the value while
 * keeping any clamp band; `validationSchema` is honoured at brand scope only.
 * A franchise/store value outside the brand clamp band rejects with 422.
 */
export async function upsertBusinessSetting(payload: UpsertSettingPayload): Promise<SettingRow> {
  const { data } = await catalogClient.put<ApiResponse<SettingRow>>(BASE, payload)
  return unwrap(data)
}

/** Clears the override row at the identified scope (value + band both removed). */
export async function clearBusinessSetting(query: ClearSettingQuery): Promise<void> {
  await catalogClient.delete(BASE, {
    params: {
      category: query.category,
      key: query.key,
      scopeType: query.scopeType,
      franchiseId: query.franchiseId,
      storeId: query.storeId,
    },
  })
}
