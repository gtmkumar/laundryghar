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

/**
 * Defensive de-dupe before the mutation response is written into the query
 * cache: the add-entry POST has been observed double-including the new entry
 * in `entries` (backend bug being fixed separately). Duplicate ids would
 * render duplicate React keys and double rows, so keep the first occurrence
 * of each entry id. Generic over the detail DTO to avoid an import cycle.
 */
function dedupeEntries<T extends { entries?: { id: string }[] | null }>(book: T): T {
  if (!book?.entries?.length) return book
  const seen = new Set<string>()
  const entries = book.entries.filter((e) => {
    if (seen.has(e.id)) return false
    seen.add(e.id)
    return true
  })
  return entries.length === book.entries.length ? book : { ...book, entries }
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
      qc.setQueryData(cashBookKeys.detail(updated.id), dedupeEntries(updated))
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
      qc.setQueryData(cashBookKeys.detail(updated.id), dedupeEntries(updated))
      void qc.invalidateQueries({ queryKey: ['cash-books', 'list'] })
    },
  })
}
