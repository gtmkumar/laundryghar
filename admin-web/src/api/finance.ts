import { financeClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  CashBookSummaryDto,
  CashBookListParams,
  ExpenseDto,
  ExpenseListParams,
  ExpenseCategoryDto,
  CreateExpensePayload,
  OpenCashBookPayload,
  RoyaltyInvoiceDto,
  RoyaltyListParams,
  GenerateRoyaltyInvoicePayload,
  IssueRoyaltyInvoicePayload,
  RecordRoyaltyPaymentPayload,
  PlatformPlanDto,
  PlatformPlanListParams,
  CreatePlatformPlanPayload,
  UpdatePlatformPlanPayload,
  FranchiseSubscriptionDto,
  FranchiseSubscriptionListParams,
  AssignFranchisePlanPayload,
} from '@/types/api'

const ADMIN = '/api/v1/admin'

// ── Cash books ────────────────────────────────────────────────────────────────

export async function getCashBooks(
  params: CashBookListParams = {},
): Promise<PaginatedList<CashBookSummaryDto>> {
  const { data } = await financeClient.get<ApiResponse<PaginatedList<CashBookSummaryDto>>>(
    `${ADMIN}/cash-books`,
    { params: { page: 1, pageSize: 100, ...params } },
  )
  return unwrapPaginated(data)
}

export async function openCashBook(payload: OpenCashBookPayload): Promise<CashBookSummaryDto> {
  const { data } = await financeClient.post<ApiResponse<CashBookSummaryDto>>(`${ADMIN}/cash-books`, payload)
  return unwrap(data)
}

// ── Expenses ────────────────────────────────────────────────────────────────

export async function getExpenses(
  params: ExpenseListParams = {},
): Promise<PaginatedList<ExpenseDto>> {
  const { data } = await financeClient.get<ApiResponse<PaginatedList<ExpenseDto>>>(
    `${ADMIN}/expenses`,
    { params: { page: 1, pageSize: 100, ...params } },
  )
  return unwrapPaginated(data)
}

export async function getExpenseCategories(): Promise<PaginatedList<ExpenseCategoryDto>> {
  const { data } = await financeClient.get<ApiResponse<PaginatedList<ExpenseCategoryDto>>>(
    `${ADMIN}/expense-categories`,
    { params: { page: 1, pageSize: 100, status: 'active' } },
  )
  return unwrapPaginated(data)
}

export async function createExpense(payload: CreateExpensePayload): Promise<ExpenseDto> {
  const { data } = await financeClient.post<ApiResponse<ExpenseDto>>(`${ADMIN}/expenses`, payload)
  return unwrap(data)
}

export async function approveExpense(id: string, notes?: string): Promise<ExpenseDto> {
  const { data } = await financeClient.post<ApiResponse<ExpenseDto>>(
    `${ADMIN}/expenses/${id}/approve`,
    { notes: notes ?? null },
  )
  return unwrap(data)
}

export async function rejectExpense(id: string, rejectionReason: string): Promise<ExpenseDto> {
  const { data } = await financeClient.post<ApiResponse<ExpenseDto>>(
    `${ADMIN}/expenses/${id}/reject`,
    { rejectionReason },
  )
  return unwrap(data)
}

export async function markExpensePaid(id: string, notes?: string): Promise<ExpenseDto> {
  const { data } = await financeClient.post<ApiResponse<ExpenseDto>>(
    `${ADMIN}/expenses/${id}/mark-paid`,
    { notes: notes ?? null },
  )
  return unwrap(data)
}

// ── Royalty invoices ─────────────────────────────────────────────────────────

export async function getRoyaltyInvoices(
  params: RoyaltyListParams = {},
): Promise<PaginatedList<RoyaltyInvoiceDto>> {
  const { data } = await financeClient.get<ApiResponse<PaginatedList<RoyaltyInvoiceDto>>>(
    `${ADMIN}/royalty-invoices`,
    { params: { page: 1, pageSize: 100, ...params } },
  )
  return unwrapPaginated(data)
}

export async function getRoyaltyInvoice(id: string): Promise<RoyaltyInvoiceDto> {
  const { data } = await financeClient.get<ApiResponse<RoyaltyInvoiceDto>>(
    `${ADMIN}/royalty-invoices/${id}`,
  )
  return unwrap(data)
}

export async function generateRoyaltyInvoice(
  payload: GenerateRoyaltyInvoicePayload,
): Promise<RoyaltyInvoiceDto> {
  const { data } = await financeClient.post<ApiResponse<RoyaltyInvoiceDto>>(
    `${ADMIN}/royalty-invoices/generate`,
    payload,
  )
  return unwrap(data)
}

export async function issueRoyaltyInvoice(
  id: string,
  payload: IssueRoyaltyInvoicePayload,
): Promise<RoyaltyInvoiceDto> {
  const { data } = await financeClient.post<ApiResponse<RoyaltyInvoiceDto>>(
    `${ADMIN}/royalty-invoices/${id}/issue`,
    payload,
  )
  return unwrap(data)
}

export async function recordRoyaltyPayment(
  id: string,
  payload: RecordRoyaltyPaymentPayload,
): Promise<RoyaltyInvoiceDto> {
  const { data } = await financeClient.post<ApiResponse<RoyaltyInvoiceDto>>(
    `${ADMIN}/royalty-invoices/${id}/record-payment`,
    payload,
  )
  return unwrap(data)
}

// ── Platform plans (SaaS — platform_admin) ─────────────────────────────────────

export async function listPlatformPlans(
  params: PlatformPlanListParams = {},
): Promise<PaginatedList<PlatformPlanDto>> {
  const { data } = await financeClient.get<ApiResponse<PaginatedList<PlatformPlanDto>>>(
    `${ADMIN}/platform-plans`,
    { params: { page: 1, pageSize: 100, ...params } },
  )
  return unwrapPaginated(data)
}

export async function createPlatformPlan(
  payload: CreatePlatformPlanPayload,
): Promise<PlatformPlanDto> {
  const { data } = await financeClient.post<ApiResponse<PlatformPlanDto>>(
    `${ADMIN}/platform-plans`,
    payload,
  )
  return unwrap(data)
}

export async function updatePlatformPlan(
  id: string,
  payload: UpdatePlatformPlanPayload,
): Promise<PlatformPlanDto> {
  const { data } = await financeClient.put<ApiResponse<PlatformPlanDto>>(
    `${ADMIN}/platform-plans/${id}`,
    payload,
  )
  return unwrap(data)
}

/** Soft-delete a platform plan. */
export async function deletePlatformPlan(id: string): Promise<void> {
  await financeClient.delete(`${ADMIN}/platform-plans/${id}`)
}

// ── Franchise subscriptions (SaaS) ─────────────────────────────────────────────

export async function listFranchiseSubscriptions(
  params: FranchiseSubscriptionListParams = {},
): Promise<PaginatedList<FranchiseSubscriptionDto>> {
  const { data } = await financeClient.get<ApiResponse<PaginatedList<FranchiseSubscriptionDto>>>(
    `${ADMIN}/franchise-subscriptions`,
    { params: { page: 1, pageSize: 100, ...params } },
  )
  return unwrapPaginated(data)
}

export async function assignFranchisePlan(
  payload: AssignFranchisePlanPayload,
): Promise<FranchiseSubscriptionDto> {
  const { data } = await financeClient.post<ApiResponse<FranchiseSubscriptionDto>>(
    `${ADMIN}/franchise-subscriptions/assign`,
    payload,
  )
  return unwrap(data)
}

export async function cancelFranchiseSubscription(
  id: string,
  reason: string | null,
): Promise<FranchiseSubscriptionDto> {
  const { data } = await financeClient.post<ApiResponse<FranchiseSubscriptionDto>>(
    `${ADMIN}/franchise-subscriptions/${id}/cancel`,
    { reason },
  )
  return unwrap(data)
}
