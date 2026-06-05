import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getCashBooks,
  getCashBookById,
  openCashBook,
  addCashBookEntry,
  closeCashBook,
} from '@/api/finance'
import type {
  CashBookListParams,
  OpenCashBookRequest,
  AddCashBookEntryRequest,
  CloseCashBookRequest,
} from '@/types/api'

export const cashBookKeys = {
  list: (params?: object) => ['cash-books', 'list', params] as const,
  detail: (id: string) => ['cash-books', 'detail', id] as const,
}

export function useCashBooks(params: CashBookListParams = {}) {
  return useQuery({
    queryKey: cashBookKeys.list(params),
    queryFn: () => getCashBooks(params),
    staleTime: 30_000,
  })
}

export function useCashBook(id: string) {
  return useQuery({
    queryKey: cashBookKeys.detail(id),
    queryFn: () => getCashBookById(id),
    enabled: Boolean(id),
    staleTime: 30_000,
  })
}

export function useOpenCashBook() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: OpenCashBookRequest) => openCashBook(req),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['cash-books', 'list'] })
    },
  })
}

export function useAddCashBookEntry() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: AddCashBookEntryRequest }) =>
      addCashBookEntry(id, req),
    onSuccess: (updated) => {
      qc.setQueryData(cashBookKeys.detail(updated.id), updated)
      void qc.invalidateQueries({ queryKey: ['cash-books', 'list'] })
    },
  })
}

export function useCloseCashBook() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: CloseCashBookRequest }) =>
      closeCashBook(id, req),
    onSuccess: (updated) => {
      qc.setQueryData(cashBookKeys.detail(updated.id), updated)
      void qc.invalidateQueries({ queryKey: ['cash-books', 'list'] })
    },
  })
}
