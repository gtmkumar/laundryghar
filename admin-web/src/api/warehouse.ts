import { warehouseClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  WarehouseBoard,
  GarmentJourneyDto,
  StockReconciliationDto,
  CreateProcessLogRequest,
  CreateGarmentRequest,
  GarmentDto,
} from '@/types/api'

const ADMIN = '/api/v1/admin'

/** Warehouse kanban read model — per-stage garment cards + header metrics. */
export async function getWarehouseBoard(): Promise<WarehouseBoard> {
  const { data } = await warehouseClient.get<ApiResponse<WarehouseBoard>>(
    `${ADMIN}/garments/board`,
  )
  return unwrap(data)
}

/**
 * Look up a garment by its physical tag code.
 * Returns the full journey DTO (garment + process logs + inspections + QC).
 * 404 → throws; the modal should catch and surface the error.
 */
export async function getGarmentByTag(tagCode: string): Promise<GarmentJourneyDto> {
  const { data } = await warehouseClient.get<ApiResponse<GarmentJourneyDto>>(
    `${ADMIN}/garments/by-tag/${encodeURIComponent(tagCode)}`,
  )
  return unwrap(data)
}

/**
 * Post a process-log (scan) event to advance a garment's stage.
 * Used by the Scan In modal to record each scanner-gun hit.
 */
export async function createProcessLog(
  req: CreateProcessLogRequest,
): Promise<void> {
  const { data } = await warehouseClient.post<ApiResponse<unknown>>(
    `${ADMIN}/process-logs`,
    req,
  )
  unwrap(data)
}

/**
 * List stock reconciliations for the active brand/warehouse (most recent first).
 */
export async function listStockReconciliations(
  page = 1,
  pageSize = 20,
  status?: string,
): Promise<PaginatedList<StockReconciliationDto>> {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  })
  if (status) params.set('status', status)

  const { data } = await warehouseClient.get<ApiResponse<PaginatedList<StockReconciliationDto>>>(
    `${ADMIN}/stock-reconciliations?${params}`,
  )
  return unwrapPaginated(data)
}

/**
 * Create a new stock reconciliation session for a warehouse.
 */
export async function createStockReconciliation(req: {
  warehouseId: string | null
  storeId: string | null
  reconDate: string     // 'YYYY-MM-DD'
  reconType: string     // 'daily' | 'adhoc' | …
}): Promise<StockReconciliationDto> {
  const { data } = await warehouseClient.post<ApiResponse<StockReconciliationDto>>(
    `${ADMIN}/stock-reconciliations`,
    req,
  )
  return unwrap(data)
}

/**
 * Register a new garment manually by order-item + tag.
 * Used by the +Add Card drawer.
 */
export async function createGarment(req: CreateGarmentRequest): Promise<GarmentDto> {
  const { data } = await warehouseClient.post<ApiResponse<GarmentDto>>(
    `${ADMIN}/garments`,
    req,
  )
  return unwrap(data)
}
