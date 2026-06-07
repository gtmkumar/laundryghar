import { warehouseClient, unwrap } from './client'
import type { ApiResponse, WarehouseBoard } from '@/types/api'

const ADMIN = '/api/v1/admin'

/** Warehouse kanban read model — per-stage garment cards + header metrics. */
export async function getWarehouseBoard(): Promise<WarehouseBoard> {
  const { data } = await warehouseClient.get<ApiResponse<WarehouseBoard>>(
    `${ADMIN}/garments/board`,
  )
  return unwrap(data)
}
