import { identityClient, logisticsClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  RiderDto,
  RiderListParams,
  UserDto,
  InviteRiderUserPayload,
  CreateRiderProfilePayload,
  UpdateRiderPayload,
  RiderLiveDto,
  RiderTrackPointDto,
  RiderStatsDto,
  RiderCodSummary,
  CodOutstandingResponse,
  RiderCodDetail,
  RiderSettlement,
  SettleRiderPayload,
  RiderVerificationDto,
  RiderDocumentDto,
} from '@/types/api'

const ACCESS = '/api/v1/admin/access-control'
const RIDERS = '/api/v1/admin/riders'
const RIDER_DOCS = '/api/v1/admin/rider-documents'

// ── List / fetch (Logistics) ─────────────────────────────────────────────────

export async function getRiders(
  page = 1,
  pageSize = 20,
  opts: Omit<RiderListParams, 'page' | 'pageSize'> = {},
): Promise<PaginatedList<RiderDto>> {
  // Drop blank filters so we never send `?search=&kycStatus=` noise to the API.
  const params: RiderListParams = { page, pageSize }
  if (opts.search) params.search = opts.search
  if (opts.kycStatus) params.kycStatus = opts.kycStatus
  if (opts.status) params.status = opts.status
  if (opts.franchiseId) params.franchiseId = opts.franchiseId
  if (opts.sort) params.sort = opts.sort

  const { data } = await logisticsClient.get<ApiResponse<PaginatedList<RiderDto>>>(RIDERS, { params })
  return unwrapPaginated(data)
}

export async function getRider(id: string): Promise<RiderDto> {
  const { data } = await logisticsClient.get<ApiResponse<RiderDto>>(`${RIDERS}/${id}`)
  return unwrap(data)
}

// ── Rider Ops live board (Logistics) ─────────────────────────────────────────

/** Live snapshot of every in-scope rider — location, ops status, today's counts. */
export async function getRidersLive(franchiseId?: string): Promise<RiderLiveDto[]> {
  const { data } = await logisticsClient.get<ApiResponse<RiderLiveDto[]>>(`${RIDERS}/live`, {
    params: franchiseId ? { franchiseId } : undefined,
  })
  return unwrap(data) ?? []
}

/** GPS breadcrumb trail for one rider on a given IST day (default today). */
export async function getRiderTrack(id: string, date?: string): Promise<RiderTrackPointDto[]> {
  const { data } = await logisticsClient.get<ApiResponse<RiderTrackPointDto[]>>(`${RIDERS}/${id}/track`, {
    params: date ? { date } : undefined,
  })
  return unwrap(data) ?? []
}

/** Per-rider throughput over a date range (default today). */
export async function getRiderStats(id: string, from?: string, to?: string): Promise<RiderStatsDto> {
  const params: Record<string, string> = {}
  if (from) params.from = from
  if (to) params.to = to
  const { data } = await logisticsClient.get<ApiResponse<RiderStatsDto>>(`${RIDERS}/${id}/stats`, { params })
  return unwrap(data)
}

// ── COD cash + settlement (Phase 3) ──────────────────────────────────────────

/** Riders with uncleared COD cash (reconciliation list). */
export async function getCodOutstanding(franchiseId?: string): Promise<RiderCodSummary[]> {
  // Backend returns the envelope { riders, totalCount, totalOutstanding }, not a bare array.
  const { data } = await logisticsClient.get<ApiResponse<CodOutstandingResponse>>(`${RIDERS}/cod/outstanding`, {
    params: franchiseId ? { franchiseId } : undefined,
  })
  return unwrap(data)?.riders ?? []
}

/** One rider's outstanding collections. */
export async function getRiderCod(id: string): Promise<RiderCodDetail> {
  const { data } = await logisticsClient.get<ApiResponse<RiderCodDetail>>(`${RIDERS}/${id}/cod`)
  return unwrap(data)
}

/** Record a settlement clearing all of a rider's outstanding COD cash. */
export async function settleRider(id: string, payload: SettleRiderPayload): Promise<RiderSettlement> {
  const { data } = await logisticsClient.post<ApiResponse<RiderSettlement>>(`${RIDERS}/${id}/settle`, payload)
  return unwrap(data)
}

/** A rider's settlement history. */
export async function getRiderSettlements(
  id: string, page = 1, pageSize = 20,
): Promise<PaginatedList<RiderSettlement>> {
  const { data } = await logisticsClient.get<ApiResponse<PaginatedList<RiderSettlement>>>(
    `${RIDERS}/${id}/settlements`, { params: { page, pageSize } },
  )
  return unwrapPaginated(data)
}

// ── Two-step onboarding ────────────────────────────────────────────────────────

/**
 * Step 1 — create the rider login in the Identity service via the dedicated
 * narrow rider-invite endpoint (so franchises can onboard their own riders).
 * Returns the created UserDto so the caller can read `id` and pass it to step 2
 * (createRiderProfile). The server forces/validates the franchise scope, so no
 * roleId or scope is supplied here.
 */
export async function inviteRiderUser(payload: InviteRiderUserPayload): Promise<UserDto> {
  const { data } = await identityClient.post<ApiResponse<UserDto>>(`${ACCESS}/riders/invite`, payload)
  return unwrap(data)
}

/** Step 2 — create the rider operational profile in the Logistics service. */
export async function createRiderProfile(payload: CreateRiderProfilePayload): Promise<RiderDto> {
  const { data } = await logisticsClient.post<ApiResponse<RiderDto>>(RIDERS, payload)
  return unwrap(data)
}

// ── Mutations (Logistics) ──────────────────────────────────────────────────────

export async function updateRider(id: string, payload: UpdateRiderPayload): Promise<RiderDto> {
  const { data } = await logisticsClient.put<ApiResponse<RiderDto>>(`${RIDERS}/${id}`, payload)
  return unwrap(data)
}

/** Approve KYC — kycStatus→'verified' (and userStatus→'active' if still invited). */
export async function verifyRider(id: string): Promise<RiderDto> {
  const { data } = await logisticsClient.post<ApiResponse<RiderDto>>(`${RIDERS}/${id}/verify`)
  return unwrap(data)
}

/** Reject KYC — kycStatus→'rejected'. Reason is optional. */
export async function rejectRider(id: string, reason?: string): Promise<RiderDto> {
  const { data } = await logisticsClient.post<ApiResponse<RiderDto>>(`${RIDERS}/${id}/reject`, {
    reason: reason || undefined,
  })
  return unwrap(data)
}

// ── Driver verification queue (KYC documents + vehicle) ──────────────────────────

/** The full review packet for one rider: KYC status, vehicle status + uploaded documents. */
export async function getRiderVerification(riderId: string): Promise<RiderVerificationDto> {
  const { data } = await logisticsClient.get<ApiResponse<RiderVerificationDto>>(
    `${RIDERS}/${riderId}/verification`,
  )
  return unwrap(data)
}

/**
 * Stream one document's file (image or PDF) as a Blob. The bearer token rides on
 * the intercepted client, so a plain <img src> can't carry it — fetch the blob
 * and hand it to URL.createObjectURL (mirrors the catalog item-image pattern).
 */
export async function getRiderDocumentBlob(docId: string): Promise<Blob> {
  const { data } = await logisticsClient.get<Blob>(`${RIDER_DOCS}/${docId}/file`, {
    responseType: 'blob',
  })
  return data
}

/** Approve one KYC document → returns the updated doc. */
export async function approveRiderDocument(docId: string): Promise<RiderDocumentDto> {
  const { data } = await logisticsClient.post<ApiResponse<RiderDocumentDto>>(
    `${RIDER_DOCS}/${docId}/approve`,
  )
  return unwrap(data)
}

/** Reject one KYC document with a reason → returns the updated doc. */
export async function rejectRiderDocument(docId: string, reason: string): Promise<RiderDocumentDto> {
  const { data } = await logisticsClient.post<ApiResponse<RiderDocumentDto>>(
    `${RIDER_DOCS}/${docId}/reject`,
    { reason },
  )
  return unwrap(data)
}

/** Approve the rider's vehicle → returns the refreshed verification view. */
export async function approveRiderVehicle(riderId: string): Promise<RiderVerificationDto> {
  const { data } = await logisticsClient.post<ApiResponse<RiderVerificationDto>>(
    `${RIDERS}/${riderId}/vehicle/approve`,
  )
  return unwrap(data)
}

/** Reject the rider's vehicle with a reason → returns the refreshed verification view. */
export async function rejectRiderVehicle(riderId: string, reason: string): Promise<RiderVerificationDto> {
  const { data } = await logisticsClient.post<ApiResponse<RiderVerificationDto>>(
    `${RIDERS}/${riderId}/vehicle/reject`,
    { reason },
  )
  return unwrap(data)
}
