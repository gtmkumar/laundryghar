import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  listSubscriptionPlans,
  createSubscriptionPlan,
  updateSubscriptionPlan,
  deleteSubscriptionPlan,
  listCustomerSubscriptions,
} from '@/api/subscriptions'
import type {
  CreateSubscriptionPlanPayload,
  UpdateSubscriptionPlanPayload,
  CustomerSubscriptionListParams,
} from '@/types/api'

export const subscriptionKeys = {
  plans: (params?: object) => ['subscriptions', 'plans', params] as const,
  customers: (params?: object) => ['subscriptions', 'customers', params] as const,
}

// ── Subscription plans ─────────────────────────────────────────────────────────

export function useSubscriptionPlans(params: { page?: number; pageSize?: number } = {}) {
  return useQuery({
    queryKey: subscriptionKeys.plans(params),
    queryFn: () => listSubscriptionPlans(params),
  })
}

export function useCreateSubscriptionPlan() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateSubscriptionPlanPayload) => createSubscriptionPlan(payload),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['subscriptions', 'plans'] }),
  })
}

export function useUpdateSubscriptionPlan() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdateSubscriptionPlanPayload }) =>
      updateSubscriptionPlan(id, payload),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['subscriptions', 'plans'] }),
  })
}

export function useDeleteSubscriptionPlan() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteSubscriptionPlan(id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['subscriptions', 'plans'] }),
  })
}

// ── Customer subscriptions (read-only) ─────────────────────────────────────────

export function useCustomerSubscriptions(params: CustomerSubscriptionListParams = {}) {
  return useQuery({
    queryKey: subscriptionKeys.customers(params),
    queryFn: () => listCustomerSubscriptions(params),
  })
}
