/**
 * Axios instance factory + interceptors.
 *
 * Responsibilities:
 *  1. Attach Authorization: Bearer <accessToken> to every request.
 *  2. Attach X-Brand-Id header when a brand context is active (required for
 *     platform_admin making brand-scoped calls; brand-scoped admins have
 *     brand_id in their JWT, but the header is harmless to include for them).
 *  3. On 401, attempt one silent token refresh then retry the original request.
 *  4. On repeated 401 (refresh failed), clear auth and redirect to /login.
 *
 * Circular import note: this module references authStore and brandStore via
 * their Zustand getState() which is safe to call synchronously at runtime
 * (stores are initialized before any request runs). We import the stores at
 * the top but only *call* getState() inside interceptors (not at module init).
 */

import axios, { type AxiosInstance, type AxiosRequestConfig } from 'axios'
import type { ApiResponse, PaginatedList, TokenResponse } from '@/types/api'
import { useAuthStore } from '@/stores/authStore'
import { useBrandStore } from '@/stores/brandStore'

const IDENTITY_URL = import.meta.env.VITE_IDENTITY_URL as string
const CATALOG_URL = import.meta.env.VITE_CATALOG_URL as string
const ORDERS_URL = import.meta.env.VITE_ORDERS_URL as string
const ENGAGEMENT_URL = import.meta.env.VITE_ENGAGEMENT_URL as string
const ANALYTICS_URL = import.meta.env.VITE_ANALYTICS_URL as string
const COMMERCE_URL = import.meta.env.VITE_COMMERCE_URL as string
const WAREHOUSE_URL = import.meta.env.VITE_WAREHOUSE_URL as string
const LOGISTICS_URL = import.meta.env.VITE_LOGISTICS_URL as string
const FINANCE_URL = import.meta.env.VITE_FINANCE_URL as string

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
  // 1. Check JWT claims first (non-platform-admin users have brand_id embedded)
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

  // Response: handle 401 with refresh + retry (once)
  instance.interceptors.response.use(
    (response) => response,
    async (error: unknown) => {
      const axiosError = error as {
        response?: { status: number }
        config: AxiosRequestConfig & { _retried?: boolean }
      }

      if (axiosError.response?.status !== 401 || axiosError.config._retried) {
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

// ── Service instances ─────────────────────────────────────────────────────────

export const identityClient = createInstance(IDENTITY_URL)
export const catalogClient = createInstance(CATALOG_URL)
export const ordersClient = createInstance(ORDERS_URL)
export const engagementClient = createInstance(ENGAGEMENT_URL)
export const analyticsClient = createInstance(ANALYTICS_URL)
export const commerceClient = createInstance(COMMERCE_URL)
export const warehouseClient = createInstance(WAREHOUSE_URL)
export const logisticsClient = createInstance(LOGISTICS_URL)
export const financeClient = createInstance(FINANCE_URL)

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
