import { financeClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  CashBookDto,
  CashBookSummaryDto,
  OpenCashBookRequest,
  AddCashBookEntryRequest,
  CloseCashBookRequest,
  CashBookListParams,
} from '@/types/api'

const ADMIN = '/api/v1/admin'

// ── List cash books ───────────────────────────────────────────────────────────
// GET /api/v1/admin/cash-books?storeId=&status=&bookDate=

export async function getCashBooks(
  params: CashBookListParams = {},
): Promise<PaginatedList<CashBookSummaryDto>> {
  const { data } = await financeClient.get<ApiResponse<PaginatedList<CashBookSummaryDto>>>(
    `${ADMIN}/cash-books`,
    { params: { page: 1, pageSize: 20, ...params } },
  )
  return unwrapPaginated(data)
}

// ── Get cash book by id (with entries) ───────────────────────────────────────
// GET /api/v1/admin/cash-books/{id}

export async function getCashBookById(id: string): Promise<CashBookDto> {
  const { data } = await financeClient.get<ApiResponse<CashBookDto>>(
    `${ADMIN}/cash-books/${id}`,
  )
  return unwrap(data)
}

// ── Open cash book ────────────────────────────────────────────────────────────
// POST /api/v1/admin/cash-books

export async function openCashBook(req: OpenCashBookRequest): Promise<CashBookDto> {
  const { data } = await financeClient.post<ApiResponse<CashBookDto>>(
    `${ADMIN}/cash-books`,
    req,
  )
  return unwrap(data)
}

// ── Add entry to cash book ────────────────────────────────────────────────────
// POST /api/v1/admin/cash-books/{id}/entries

export async function addCashBookEntry(
  id: string,
  req: AddCashBookEntryRequest,
): Promise<CashBookDto> {
  const { data } = await financeClient.post<ApiResponse<CashBookDto>>(
    `${ADMIN}/cash-books/${id}/entries`,
    req,
  )
  return unwrap(data)
}

// ── Close cash book ───────────────────────────────────────────────────────────
// POST /api/v1/admin/cash-books/{id}/close

export async function closeCashBook(
  id: string,
  req: CloseCashBookRequest,
): Promise<CashBookDto> {
  const { data } = await financeClient.post<ApiResponse<CashBookDto>>(
    `${ADMIN}/cash-books/${id}/close`,
    req,
  )
  return unwrap(data)
}
