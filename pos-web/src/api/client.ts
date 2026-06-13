/**
 * Axios instance factory + interceptors for POS Web.
 *
 * Responsibilities:
 *  1. Attach Authorization: Bearer <accessToken> to every request.
 *  2. Attach X-Brand-Id header when a brand context is active (required for
 *     platform_admin; store staff have brand_id in their JWT — header is harmless).
 *  3. On 401, attempt one silent token refresh then retry the original request.
 *  4. On repeated 401 (refresh failed), clear auth and redirect to /login.
 *
 * Five service clients: identity, catalog, orders, finance, commerce.
 * Base URLs come from VITE_*_URL env vars — in dev these point at the API
 * gateway (http://localhost:8080/<prefix>), which fans in to the hosts.
 */

import axios, { type AxiosInstance, type AxiosRequestConfig } from 'axios'
import type { ApiResponse, PaginatedList, TokenResponse } from '@/types/api'
import { useAuthStore } from '@/stores/authStore'
import { useBrandStore } from '@/stores/brandStore'

const IDENTITY_URL = import.meta.env.VITE_IDENTITY_URL as string
const CATALOG_URL = import.meta.env.VITE_CATALOG_URL as string
const ORDERS_URL = import.meta.env.VITE_ORDERS_URL as string
const FINANCE_URL = import.meta.env.VITE_FINANCE_URL as string
const COMMERCE_URL = import.meta.env.VITE_COMMERCE_URL as string

// ── Token refresh state ───────────────────────────────────────────────────────

let isRefreshing = false
let pendingQueue: Array<{
  resolve: (token: string) => void
  reject: (err: unknown) => void
}> = []

function drainQueue(token: string) {
  pendingQueue.forEach((p) => p.resolve(token))
  pendingQueue = []
}

function rejectQueue(err: unknown) {
  pendingQueue.forEach((p) => p.reject(err))
  pendingQueue = []
}

// ── Store accessors (called at request-time, not module-init time) ────────────

function getAuthState() {
  return useAuthStore.getState()
}

function getActiveBrandId(): string | null {
  // 1. Check JWT claims first (store/brand staff have brand_id embedded)
  const { accessToken } = getAuthState()
  if (accessToken) {
    try {
      const payload = JSON.parse(
        atob(accessToken.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')),
      ) as { brand_id?: string }
      if (payload.brand_id) return payload.brand_id
    } catch {
      // ignore parse errors
    }
  }
  // 2. Fall back to the platform-admin's manual brand selection
  return useBrandStore.getState().activeBrandId
}

// ── Instance factory ──────────────────────────────────────────────────────────

function createInstance(baseURL: string): AxiosInstance {
  const instance = axios.create({
    baseURL,
    headers: { 'Content-Type': 'application/json' },
    timeout: 30_000,
  })

  // Request: attach token + brand header
  instance.interceptors.request.use((config) => {
    const { accessToken } = getAuthState()
    if (accessToken) {
      config.headers['Authorization'] = `Bearer ${accessToken}`
    }
    const brandId = getActiveBrandId()
    if (brandId) {
      config.headers['X-Brand-Id'] = brandId
    }
    return config
  })

  // Response: handle 401 with refresh + retry (once).
  // IMPORTANT: Some endpoints return 401 for "brand context required" (X-Brand-Id missing).
  // These are authorization-context errors, NOT expired-token errors. Attempting a token
  // refresh in that case would waste a token rotation and leave the app stuck in a retry loop.
  // We detect the backend's known error code for this case and skip the refresh path.
  instance.interceptors.response.use(
    (response) => response,
    async (error: unknown) => {
      const axiosError = error as {
        response?: {
          status: number
          data?: { message?: { errorMessage?: Record<string, string[]> } }
        }
        config: AxiosRequestConfig & { _retried?: boolean }
      }

      if (axiosError.response?.status !== 401 || axiosError.config._retried) {
        return Promise.reject(error)
      }

      // Brand-context 401: skip refresh, propagate the error as-is.
      const errorMessages = axiosError.response?.data?.message?.errorMessage ?? {}
      const isBrandContextError = Object.values(errorMessages).flat().some(
        (msg) => typeof msg === 'string' && msg.toLowerCase().includes('brand context'),
      )
      if (isBrandContextError) {
        return Promise.reject(error)
      }

      axiosError.config._retried = true

      if (isRefreshing) {
        return new Promise((resolve, reject) => {
          pendingQueue.push({
            resolve: (token) => {
              if (axiosError.config.headers) {
                axiosError.config.headers['Authorization'] = `Bearer ${token}`
              }
              resolve(instance(axiosError.config))
            },
            reject,
          })
        })
      }

      isRefreshing = true

      try {
        const { refreshToken, setTokens } = getAuthState()
        if (!refreshToken) throw new Error('No refresh token available')

        // Plain axios call — bypasses our intercepted instance to avoid recursion
        const { data } = await axios.post<ApiResponse<TokenResponse>>(
          `${IDENTITY_URL}/api/v1/auth/refresh`,
          { refreshToken },
        )

        if (!data.status || !data.data) throw new Error('Token refresh failed')

        const { accessToken: newAccess, refreshToken: newRefresh } = data.data
        setTokens(newAccess, newRefresh)
        drainQueue(newAccess)

        if (axiosError.config.headers) {
          axiosError.config.headers['Authorization'] = `Bearer ${newAccess}`
        }
        return instance(axiosError.config)
      } catch (refreshError) {
        rejectQueue(refreshError)
        getAuthState().clearAuth()
        window.location.replace('/login')
        return Promise.reject(refreshError)
      } finally {
        isRefreshing = false
      }
    },
  )

  return instance
}

/**
 * POS-2: standalone silent refresh used by ProtectedRoute to proactively rotate
 * a stale/near-expiry access token on hard reload or after a long-idle tab,
 * BEFORE the first API call would 401. Uses the in-memory refresh token when
 * present; the HttpOnly `lg_refresh` cookie is the durable fallback across a
 * hard reload. Bypasses the intercepted instances (plain axios) to avoid
 * recursing into the 401 handler. Throws on failure so the caller can logout.
 */
export async function refreshAccessToken(): Promise<string> {
  const { refreshToken, setTokens } = getAuthState()
  const { data } = await axios.post<ApiResponse<TokenResponse>>(
    `${IDENTITY_URL}/api/v1/auth/refresh`,
    // Body carries the in-memory token when we still have it; the cookie covers
    // the hard-reload case where it was dropped.
    refreshToken ? { refreshToken } : {},
    { withCredentials: true },
  )
  if (!data.status || !data.data) throw new Error('Token refresh failed')
  const { accessToken: newAccess, refreshToken: newRefresh } = data.data
  setTokens(newAccess, newRefresh)
  return newAccess
}

// ── Service instances ─────────────────────────────────────────────────────────

export const identityClient = createInstance(IDENTITY_URL)
export const catalogClient = createInstance(CATALOG_URL)
export const ordersClient = createInstance(ORDERS_URL)
export const financeClient = createInstance(FINANCE_URL)
export const commerceClient = createInstance(COMMERCE_URL)

// ── Response envelope helpers ─────────────────────────────────────────────────

/**
 * Unwraps the backend response envelope { status, data, message }.
 * Throws with a human-readable message if status is false or data is null.
 */
export function unwrap<T>(response: ApiResponse<T>): T {
  if (!response.status || response.data === null || response.data === undefined) {
    const msg =
      response.message?.responseMessage ??
      Object.values(response.message?.errorMessage ?? {}).flat().join(', ') ??
      'An unexpected error occurred.'
    throw new Error(msg)
  }
  return response.data
}

export function unwrapPaginated<T>(response: ApiResponse<PaginatedList<T>>) {
  return unwrap(response)
}
