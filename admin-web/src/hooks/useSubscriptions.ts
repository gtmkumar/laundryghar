import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  listSubscriptionPlans,
  createSubscriptionPlan,
  updateSubscriptionPlan,
  patchSubscriptionPlanStatus,
  deleteSubscriptionPlan,
  listCustomerSubscriptions,
  patchCustomerSubscriptionStatus,
  type CustomerSubscriptionStatus,
} from '@/api/subscriptions'
import type {
  CreateSubscriptionPlanPayload,
  UpdateSubscriptionPlanPayload,
  CustomerSubscriptionListParams,
  SubscriptionPlanDto,
  CustomerSubscriptionDto,
} from '@/types/api'
import { patchListItem, removeListItem, rollbackWithToast } from '@/lib/optimistic'

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

/**
 * Status-only transition hook — PATCHes `{ status }` instead of re-PUTting the
 * full DTO, so a concurrent field edit isn't reverted (WEB-6).
 */
export function usePatchSubscriptionPlanStatus() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, status }: { id: string; status: string }) =>
      patchSubscriptionPlanStatus(id, status),
    onMutate: ({ id, status }) =>
      patchListItem<SubscriptionPlanDto>(qc, [['subscriptions', 'plans']], id, { status }),
    onError: (error, _v, ctx) => rollbackWithToast(ctx, error),
    onSettled: () => void qc.invalidateQueries({ queryKey: ['subscriptions', 'plans'] }),
  })
}

export function useDeleteSubscriptionPlan() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteSubscriptionPlan(id),
    onMutate: (id) => removeListItem(qc, [['subscriptions', 'plans']], id),
    onError: (error, _v, ctx) => rollbackWithToast(ctx, error),
    onSettled: () => void qc.invalidateQueries({ queryKey: ['subscriptions', 'plans'] }),
  })
}

// ── Customer subscriptions (read-only) ─────────────────────────────────────────

export function useCustomerSubscriptions(params: CustomerSubscriptionListParams = {}) {
  return useQuery({
    queryKey: subscriptionKeys.customers(params),
    queryFn: () => listCustomerSubscriptions(params),
  })
}

const CUSTOMER_SUBS_PAGE_SIZE = 50

/**
 * Infinite-scroll customer subscriptions (house pattern). Customer subscriptions
 * grow unbounded over a brand's lifetime, so the old hard pageSize-100 fetch
 * silently dropped rows past the cap. Flatten `data.pages.flatMap(p => p.list)`;
 * `totalCount` comes off the first page (same on every page).
 */
export function useCustomerSubscriptionsInfinite(
  params: Omit<CustomerSubscriptionListParams, 'page' | 'pageSize'> = {},
) {
  return useInfiniteQuery({
    queryKey: subscriptionKeys.customers({ ...params, _infinite: true }),
    queryFn: ({ pageParam }) =>
      listCustomerSubscriptions({ ...params, page: pageParam, pageSize: CUSTOMER_SUBS_PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.hasNextPage ? allPages.length + 1 : undefined,
  })
}

/**
 * Cancel / pause / resume a customer subscription via the narrow status PATCH.
 * Invalidates every customer-subscription list so the drawer's row and counts
 * reflect the new status. Requires the `subscription.manage` permission (also
 * enforced server-side).
 */
export function usePatchCustomerSubscriptionStatus() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { id: string; status: CustomerSubscriptionStatus; expectedUpdatedAt?: string }) =>
      patchCustomerSubscriptionStatus(v.id, v.status, v.expectedUpdatedAt),
    // Prefix covers both the plain and infinite customer-subscription lists. A
    // stale expectedUpdatedAt 409s; rollbackWithToast reverts and surfaces it.
    onMutate: ({ id, status }) =>
      patchListItem<CustomerSubscriptionDto>(qc, [['subscriptions', 'customers']], id, { status }),
    onError: (error, _v, ctx) => rollbackWithToast(ctx, error),
    onSettled: () => void qc.invalidateQueries({ queryKey: ['subscriptions', 'customers'] }),
  })
}
