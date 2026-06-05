/**
 * Typed API clients — one axios instance per microservice.
 * All rider requests send  Authorization: Bearer <token>.
 * 401 triggers one token refresh attempt, then logout.
 *
 * Identical pattern to customer-mobile/src/api/client.ts — same
 * refresh-coalescing strategy, same unwrap helpers.
 */
import axios, {
  AxiosInstance,
  AxiosRequestConfig,
  AxiosResponse,
  InternalAxiosRequestConfig,
} from 'axios';
import { CONFIG } from '@/constants/config';
import type {
  BaseResponse,
  ListResponse,
  PaginatedListResponse,
  SingleResponse,
} from '@/types/api';
import { refreshAccessToken } from '@/api/auth';

// ---------------------------------------------------------------------------
// Token storage — injected at app boot by the auth store
// ---------------------------------------------------------------------------

let _getAccessToken:  (() => string | null) | null = null;
let _getRefreshToken: (() => string | null) | null = null;
let _onAuthFailure:   (() => void)          | null = null;

export function configureApiAuth(opts: {
  getAccessToken:  () => string | null;
  getRefreshToken: () => string | null;
  onAuthFailure:   () => void;
}): void {
  _getAccessToken  = opts.getAccessToken;
  _getRefreshToken = opts.getRefreshToken;
  _onAuthFailure   = opts.onAuthFailure;
}

// ---------------------------------------------------------------------------
// ApiError
// ---------------------------------------------------------------------------

export class ApiError extends Error {
  public readonly status: boolean;
  public readonly errorCode?: number;
  public readonly fieldErrors?: Record<string, string[]>;

  constructor(
    message: string,
    opts: {
      status?:      boolean;
      errorCode?:   number;
      fieldErrors?: Record<string, string[]>;
    } = {},
  ) {
    super(message);
    this.name       = 'ApiError';
    this.status     = opts.status ?? false;
    this.errorCode  = opts.errorCode;
    this.fieldErrors = opts.fieldErrors;
  }
}

// ---------------------------------------------------------------------------
// Envelope unwrappers
// ---------------------------------------------------------------------------

/** Unwraps a SingleResponse envelope. Throws ApiError when status=false. */
export function unwrapSingle<T>(res: SingleResponse<T>): T {
  if (!res.status || res.data == null) {
    throw new ApiError(
      res.message?.responseMessage ?? 'API error',
      {
        status:      res.status,
        errorCode:   res.message?.errorTypeCode,
        fieldErrors: res.message?.errorMessage,
      },
    );
  }
  return res.data;
}

/** Unwraps a ListResponse envelope — returns T[] (empty array when data absent). */
export function unwrapList<T>(res: ListResponse<T>): T[] {
  if (!res.status) {
    throw new ApiError(
      res.message?.responseMessage ?? 'API error',
      {
        status:    res.status,
        errorCode: res.message?.errorTypeCode,
        fieldErrors: res.message?.errorMessage,
      },
    );
  }
  return res.data ?? [];
}

/** Unwraps a PaginatedListResponse — returns { list, hasPreviousPage, hasNextPage }. */
export function unwrapPaginated<T>(res: PaginatedListResponse<T>): {
  list: T[];
  hasPreviousPage: boolean;
  hasNextPage: boolean;
} {
  if (!res.status || res.data == null) {
    throw new ApiError(
      res.message?.responseMessage ?? 'API error',
      { status: res.status },
    );
  }
  return {
    list:            res.data.list ?? [],
    hasPreviousPage: res.data.hasPreviousPage,
    hasNextPage:     res.data.hasNextPage,
  };
}

// ---------------------------------------------------------------------------
// Coalesced refresh — prevents multiple concurrent refresh calls
// ---------------------------------------------------------------------------

let refreshPromise: Promise<string> | null = null;

// ---------------------------------------------------------------------------
// Factory
// ---------------------------------------------------------------------------

function createAxiosInstance(baseURL: string): AxiosInstance {
  const instance = axios.create({
    baseURL,
    timeout: 15_000,
    headers: { 'Content-Type': 'application/json' },
  });

  // Request: attach Bearer token
  instance.interceptors.request.use(
    (config: InternalAxiosRequestConfig) => {
      const token = _getAccessToken?.();
      if (token) {
        config.headers.Authorization = `Bearer ${token}`;
      }
      return config;
    },
    (err) => Promise.reject(err),
  );

  // Response: 401 → refresh once → retry
  instance.interceptors.response.use(
    (response: AxiosResponse) => response,
    async (error) => {
      const originalRequest = error.config as InternalAxiosRequestConfig & {
        _retried?: boolean;
      };

      if (error.response?.status === 401 && !originalRequest._retried) {
        originalRequest._retried = true;

        try {
          if (!refreshPromise) {
            const refreshToken = _getRefreshToken?.();
            if (!refreshToken) throw new Error('No refresh token');
            refreshPromise = refreshAccessToken(refreshToken).finally(() => {
              refreshPromise = null;
            });
          }

          const newAccessToken = await refreshPromise;
          originalRequest.headers.Authorization = `Bearer ${newAccessToken}`;
          return instance(originalRequest);
        } catch {
          _onAuthFailure?.();
          return Promise.reject(error);
        }
      }

      return Promise.reject(error);
    },
  );

  return instance;
}

// ---------------------------------------------------------------------------
// Per-service instances
// ---------------------------------------------------------------------------

/** Identity service — used for auth/password/login, auth/refresh, auth/logout */
export const identityClient  = createAxiosInstance(`${CONFIG.identityApiUrl}/api/v1`);

/** Logistics service — all /rider/* self-service endpoints */
export const logisticsClient = createAxiosInstance(`${CONFIG.logisticsApiUrl}/api/v1`);
