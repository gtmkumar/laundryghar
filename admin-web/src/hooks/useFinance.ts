import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getCashBooks,
  getCashBook,
  openCashBook,
  closeCashBook,
  getShiftHandovers,
  createShiftHandover,
  getExpenses,
  getExpenseCategories,
  createExpense,
  approveExpense,
  rejectExpense,
  markExpensePaid,
  getRoyaltyInvoices,
  getRoyaltyInvoice,
  generateRoyaltyInvoice,
  issueRoyaltyInvoice,
  recordRoyaltyPayment,
  listPlatformPlans,
  createPlatformPlan,
  updatePlatformPlan,
  patchPlatformPlanStatus,
  deletePlatformPlan,
  listFranchiseSubscriptions,
  assignFranchisePlan,
  cancelFranchiseSubscription,
} from '@/api/finance'
import type {
  CashBookListParams,
  CloseCashBookPayload,
  ShiftHandoverListParams,
  CreateShiftHandoverPayload,
  ExpenseListParams,
  CreateExpensePayload,
  OpenCashBookPayload,
  RoyaltyListParams,
  GenerateRoyaltyInvoicePayload,
  IssueRoyaltyInvoicePayload,
  RecordRoyaltyPaymentPayload,
  PlatformPlanListParams,
  CreatePlatformPlanPayload,
  UpdatePlatformPlanPayload,
  FranchiseSubscriptionListParams,
  AssignFranchisePlanPayload,
} from '@/types/api'

export const financeKeys = {
  cashBooks: (params?: object) => ['finance', 'cashBooks', params] as const,
  cashBook: (id: string) => ['finance', 'cashBook', id] as const,
  shiftHandovers: (params?: object) => ['finance', 'shiftHandovers', params] as const,
  expenses: (params?: object) => ['finance', 'expenses', params] as const,
  expenseCategories: () => ['finance', 'expenseCategories'] as const,
  royaltyInvoices: (params?: object) => ['finance', 'royaltyInvoices', params] as const,
  royaltyInvoice: (id: string) => ['finance', 'royaltyInvoice', id] as const,
  platformPlans: (params?: object) => ['finance', 'platformPlans', params] as const,
  franchiseSubscriptions: (params?: object) => ['finance', 'franchiseSubscriptions', params] as const,
}

export function useCashBooks(params: CashBookListParams = {}) {
  return useQuery({
    queryKey: financeKeys.cashBooks(params),
    queryFn: () => getCashBooks(params),
  })
}

export function useOpenCashBook() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: OpenCashBookPayload) => openCashBook(payload),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['finance', 'cashBooks'] }),
  })
}

export function useCashBook(id: string | null) {
  return useQuery({
    queryKey: financeKeys.cashBook(id ?? ''),
    queryFn: () => getCashBook(id!),
    enabled: !!id,
  })
}

export function useCloseCashBook() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: CloseCashBookPayload }) =>
      closeCashBook(id, payload),
    onSuccess: (_data, vars) => {
      void qc.invalidateQueries({ queryKey: ['finance', 'cashBooks'] })
      void qc.invalidateQueries({ queryKey: financeKeys.cashBook(vars.id) })
    },
  })
}

export function useShiftHandovers(params: ShiftHandoverListParams = {}) {
  return useQuery({
    queryKey: financeKeys.shiftHandovers(params),
    queryFn: () => getShiftHandovers(params),
  })
}

export function useCreateShiftHandover() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateShiftHandoverPayload) => createShiftHandover(payload),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['finance', 'shiftHandovers'] }),
  })
}

export function useExpenses(params: ExpenseListParams = {}) {
  return useQuery({
    queryKey: financeKeys.expenses(params),
    queryFn: () => getExpenses(params),
  })
}

export function useExpenseCategories() {
  return useQuery({
    queryKey: financeKeys.expenseCategories(),
    queryFn: getExpenseCategories,
    staleTime: 5 * 60_000,
  })
}

export function useCreateExpense() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateExpensePayload) => createExpense(payload),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['finance', 'expenses'] }),
  })
}

/** Approve / reject / mark-paid all refresh the expense list. */
export function useExpenseAction() {
  const qc = useQueryClient()
  const refresh = () => void qc.invalidateQueries({ queryKey: ['finance', 'expenses'] })
  return {
    approve: useMutation({
      mutationFn: ({ id, notes }: { id: string; notes?: string }) => approveExpense(id, notes),
      onSuccess: refresh,
    }),
    reject: useMutation({
      mutationFn: ({ id, reason }: { id: string; reason: string }) => rejectExpense(id, reason),
      onSuccess: refresh,
    }),
    markPaid: useMutation({
      mutationFn: ({ id, notes }: { id: string; notes?: string }) => markExpensePaid(id, notes),
      onSuccess: refresh,
    }),
  }
}

// ── Royalty hooks ─────────────────────────────────────────────────────────────

export function useRoyaltyInvoices(params: RoyaltyListParams = {}) {
  return useQuery({
    queryKey: financeKeys.royaltyInvoices(params),
    queryFn: () => getRoyaltyInvoices(params),
  })
}

export function useRoyaltyInvoice(id: string | null) {
  return useQuery({
    queryKey: financeKeys.royaltyInvoice(id ?? ''),
    queryFn: () => getRoyaltyInvoice(id!),
    enabled: !!id,
  })
}

export function useGenerateRoyaltyInvoice() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: GenerateRoyaltyInvoicePayload) => generateRoyaltyInvoice(payload),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['finance', 'royaltyInvoices'] }),
  })
}

export function useRoyaltyInvoiceActions() {
  const qc = useQueryClient()
  // Invalidate BOTH the list and the open invoice's detail query, so the drawer
  // doesn't keep showing a stale status / outstanding balance after issue or a
  // recorded payment (R3-AW-4: the prior `refresh` invalidated only the list).
  const refresh = (id: string) => {
    void qc.invalidateQueries({ queryKey: ['finance', 'royaltyInvoices'] })
    void qc.invalidateQueries({ queryKey: financeKeys.royaltyInvoice(id) })
  }

  return {
    issue: useMutation({
      mutationFn: ({ id, payload }: { id: string; payload: IssueRoyaltyInvoicePayload }) =>
        issueRoyaltyInvoice(id, payload),
      onSuccess: (_data, { id }) => refresh(id),
    }),
    recordPayment: useMutation({
      mutationFn: ({ id, payload }: { id: string; payload: RecordRoyaltyPaymentPayload }) =>
        recordRoyaltyPayment(id, payload),
      onSuccess: (_data, { id }) => refresh(id),
    }),
  }
}

// ── Platform plans (SaaS) ──────────────────────────────────────────────────────

export function usePlatformPlans(params: PlatformPlanListParams = {}) {
  return useQuery({
    queryKey: financeKeys.platformPlans(params),
    queryFn: () => listPlatformPlans(params),
  })
}

export function useCreatePlatformPlan() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreatePlatformPlanPayload) => createPlatformPlan(payload),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['finance', 'platformPlans'] }),
  })
}

export function useUpdatePlatformPlan() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdatePlatformPlanPayload }) =>
      updatePlatformPlan(id, payload),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['finance', 'platformPlans'] }),
  })
}

/**
 * Status-only transition hook — PATCHes `{ status }` instead of re-PUTting the
 * full DTO, so a concurrent field edit isn't reverted (WEB-6).
 */
export function usePatchPlatformPlanStatus() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, status }: { id: string; status: string }) =>
      patchPlatformPlanStatus(id, status),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['finance', 'platformPlans'] }),
  })
}

export function useDeletePlatformPlan() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deletePlatformPlan(id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['finance', 'platformPlans'] }),
  })
}

// ── Franchise subscriptions (SaaS) ─────────────────────────────────────────────

export function useFranchiseSubscriptions(params: FranchiseSubscriptionListParams = {}) {
  return useQuery({
    queryKey: financeKeys.franchiseSubscriptions(params),
    queryFn: () => listFranchiseSubscriptions(params),
  })
}

export function useFranchiseSubscriptionActions() {
  const qc = useQueryClient()
  const refresh = () => void qc.invalidateQueries({ queryKey: ['finance', 'franchiseSubscriptions'] })
  return {
    assign: useMutation({
      mutationFn: (payload: AssignFranchisePlanPayload) => assignFranchisePlan(payload),
      onSuccess: refresh,
    }),
    cancel: useMutation({
      mutationFn: ({ id, reason }: { id: string; reason: string | null }) =>
        cancelFranchiseSubscription(id, reason),
      onSuccess: refresh,
    }),
  }
}
