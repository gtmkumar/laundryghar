import { commerceClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  SubscriptionPlanDto,
  CreateSubscriptionPlanPayload,
  UpdateSubscriptionPlanPayload,
  CustomerSubscriptionDto,
  CustomerSubscriptionListParams,
} from '@/types/api'

const ADMIN = '/api/v1/admin'

// ── Subscription plans (Commerce admin) ─────────────────────────────────────────

export async function listSubscriptionPlans(
  params: { page?: number; pageSize?: number } = {},
): Promise<PaginatedList<SubscriptionPlanDto>> {
  const { data } = await commerceClient.get<ApiResponse<PaginatedList<SubscriptionPlanDto>>>(
    `${ADMIN}/subscription-plans`,
    { params: { page: 1, pageSize: 100, ...params } },
  )
  return unwrapPaginated(data)
}

export async function createSubscriptionPlan(
  payload: CreateSubscriptionPlanPayload,
): Promise<SubscriptionPlanDto> {
  const { data } = await commerceClient.post<ApiResponse<SubscriptionPlanDto>>(
    `${ADMIN}/subscription-plans`,
    payload,
  )
  return unwrap(data)
}

export async function updateSubscriptionPlan(
  id: string,
  payload: UpdateSubscriptionPlanPayload,
): Promise<SubscriptionPlanDto> {
  const { data } = await commerceClient.put<ApiResponse<SubscriptionPlanDto>>(
    `${ADMIN}/subscription-plans/${id}`,
    payload,
  )
  return unwrap(data)
}

/**
 * Status-only transition (publish/pause/archive). PATCHes just `{ status }` so a
 * concurrent edit to price/quota/features is NOT clobbered by re-POSTing a stale
 * full DTO — the backend bumps the row Version itself (WEB-6 lost-update fix).
 */
export async function patchSubscriptionPlanStatus(
  id: string,
  status: string,
): Promise<SubscriptionPlanDto> {
  const { data } = await commerceClient.patch<ApiResponse<SubscriptionPlanDto>>(
    `${ADMIN}/subscription-plans/${id}/status`,
    { status },
  )
  return unwrap(data)
}

/** Soft-delete a subscription plan (only when it has no active subscribers). */
export async function deleteSubscriptionPlan(id: string): Promise<void> {
  await commerceClient.delete(`${ADMIN}/subscription-plans/${id}`)
}

// ── Customer subscriptions (admin list + status patch) ─────────────────────────

export async function listCustomerSubscriptions(
  params: CustomerSubscriptionListParams = {},
): Promise<PaginatedList<CustomerSubscriptionDto>> {
  const { data } = await commerceClient.get<ApiResponse<PaginatedList<CustomerSubscriptionDto>>>(
    `${ADMIN}/subscriptions`,
    { params: { page: 1, pageSize: 100, ...params } },
  )
  return unwrapPaginated(data)
}

/** Backend-accepted target statuses for a customer-subscription status PATCH. */
export type CustomerSubscriptionStatus = 'active' | 'suspended' | 'cancelled' | 'paused'

/**
 * Narrow status-only update for a customer subscription (PATCH
 * /admin/subscriptions/{id}/status, gated subscription.manage). Passes the
 * caller's last-seen `updatedAt` as `expectedUpdatedAt` so the backend rejects a
 * stale write (optimistic concurrency → 409 BusinessRuleException). Drives the
 * Cancel / Pause / Resume footer actions on the detail drawer.
 *
 * Backed by AdminCommerceEndpoints.cs PATCH .../status +
 * PatchCustomerSubscriptionStatusCommand (already merged).
 */
export async function patchCustomerSubscriptionStatus(
  id: string,
  status: CustomerSubscriptionStatus,
  expectedUpdatedAt?: string,
): Promise<CustomerSubscriptionDto> {
  const { data } = await commerceClient.patch<ApiResponse<CustomerSubscriptionDto>>(
    `${ADMIN}/subscriptions/${id}/status`,
    { status, expectedUpdatedAt },
  )
  return unwrap(data)
}
