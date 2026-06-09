import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getCashBooks,
  openCashBook,
  getExpenses,
  getExpenseCategories,
  createExpense,
  approveExpense,
  rejectExpense,
  markExpensePaid,
} from '@/api/finance'
import type {
  CashBookListParams,
  ExpenseListParams,
  CreateExpensePayload,
  OpenCashBookPayload,
} from '@/types/api'

export const financeKeys = {
  cashBooks: (params?: object) => ['finance', 'cashBooks', params] as const,
  expenses: (params?: object) => ['finance', 'expenses', params] as const,
  expenseCategories: () => ['finance', 'expenseCategories'] as const,
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
