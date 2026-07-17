import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getIncentiveRules,
  createIncentiveRule,
  updateIncentiveRule,
  deleteIncentiveRule,
} from '@/api/incentives'
import type { CreateIncentiveRulePayload, UpdateIncentiveRulePayload } from '@/types/api'
import { removeListItem, rollbackWithToast } from '@/lib/optimistic'
import { useEffectiveBrandId } from './useBrandContext'

/** All incentive rules; pass `activeOnly` to hide inactive/expired ones. */
export function useIncentiveRules(activeOnly = false) {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: ['incentive-rules', brandId, activeOnly],
    queryFn: () => getIncentiveRules(activeOnly),
    enabled: !!brandId,
  })
}

function invalidateRules(qc: ReturnType<typeof useQueryClient>) {
  qc.invalidateQueries({ queryKey: ['incentive-rules'] })
}

export function useCreateIncentiveRule() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateIncentiveRulePayload) => createIncentiveRule(payload),
    onSuccess: () => invalidateRules(qc),
  })
}

export function useUpdateIncentiveRule() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdateIncentiveRulePayload }) =>
      updateIncentiveRule(id, payload),
    onSuccess: () => invalidateRules(qc),
  })
}

export function useDeleteIncentiveRule() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteIncentiveRule(id),
    onMutate: (id) => removeListItem(qc, [['incentive-rules']], id),
    onError: (error, _v, ctx) => rollbackWithToast(ctx, error),
    onSettled: () => invalidateRules(qc),
  })
}
