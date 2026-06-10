import { catalogClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  AdminCustomerDto,
  AdminCustomerListParams,
  AdminCreateCustomerRequest,
} from '@/types/api'

const ADMIN = '/api/v1/admin'

// ── List / search customers ───────────────────────────────────────────────────
// GET /api/v1/admin/customers?page=&pageSize=&status=&search=
// `search` matches phone / name / customer code (server-side).

export async function getAdminCustomers(
  params: AdminCustomerListParams = {},
): Promise<PaginatedList<AdminCustomerDto>> {
  const { data } = await catalogClient.get<ApiResponse<PaginatedList<AdminCustomerDto>>>(
    `${ADMIN}/customers`,
    { params: { page: 1, pageSize: 20, ...params } },
  )
  return unwrapPaginated(data)
}

// ── Create customer (admin / counter) ─────────────────────────────────────────
// POST /api/v1/admin/customers
// Requires permission:customer.create. Returns 422 on duplicate phone.

export async function createAdminCustomer(
  req: AdminCreateCustomerRequest,
): Promise<AdminCustomerDto> {
  const { data } = await catalogClient.post<ApiResponse<AdminCustomerDto>>(
    `${ADMIN}/customers`,
    req,
  )
  return unwrap(data)
}
