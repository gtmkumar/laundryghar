import { logisticsClient, unwrap } from './client'
import type {
  ApiResponse,
  IncentiveRuleDto,
  CreateIncentiveRulePayload,
  UpdateIncentiveRulePayload,
} from '@/types/api'

const RULES = '/api/v1/admin/incentive-rules'

/** Rider incentive rules. Pass `activeOnly` to hide inactive/expired rules. */
export async function getIncentiveRules(activeOnly = false): Promise<IncentiveRuleDto[]> {
  const { data } = await logisticsClient.get<ApiResponse<IncentiveRuleDto[]>>(RULES, {
    params: activeOnly ? { activeOnly: true } : undefined,
  })
  return unwrap(data) ?? []
}

export async function createIncentiveRule(
  payload: CreateIncentiveRulePayload,
): Promise<IncentiveRuleDto> {
  const { data } = await logisticsClient.post<ApiResponse<IncentiveRuleDto>>(RULES, payload)
  return unwrap(data)
}

export async function updateIncentiveRule(
  id: string,
  payload: UpdateIncentiveRulePayload,
): Promise<IncentiveRuleDto> {
  const { data } = await logisticsClient.put<ApiResponse<IncentiveRuleDto>>(`${RULES}/${id}`, payload)
  return unwrap(data)
}

export async function deleteIncentiveRule(id: string): Promise<void> {
  await logisticsClient.delete<ApiResponse<unknown>>(`${RULES}/${id}`)
}
