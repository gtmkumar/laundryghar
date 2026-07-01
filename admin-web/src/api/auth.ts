import type { AxiosRequestConfig } from 'axios'
import { identityClient, unwrap } from './client'
import { useAuthStore } from '@/stores/authStore'
import type {
  ApiResponse,
  TokenResponse,
  PasswordLoginRequest,
  RefreshTokenRequest,
  OtpSentResponse,
  StepUpTokenResponse,
  StepUpIdentifierType,
} from '@/types/api'

const BASE = '/api/v1/auth'

/** Opts a request out of the interceptor's 401 refresh-and-retry (see api/client.ts). */
const SKIP_AUTH_RETRY = { _skipAuthRetry: true } as AxiosRequestConfig & { _skipAuthRetry: boolean }

export async function passwordLogin(req: PasswordLoginRequest): Promise<TokenResponse> {
  const { data } = await identityClient.post<ApiResponse<TokenResponse>>(
    `${BASE}/password/login`,
    req,
  )
  return unwrap(data)
}

/**
 * Refresh against Identity. The refresh token is supplied by the HttpOnly
 * `lg_refresh` cookie (withCredentials), with an optional in-memory body token
 * for backward compat. Most refreshes go through refreshAccessToken() in
 * api/client.ts; this remains for any direct caller.
 */
export async function refreshTokens(req: RefreshTokenRequest): Promise<TokenResponse> {
  const { data } = await identityClient.post<ApiResponse<TokenResponse>>(
    `${BASE}/refresh`,
    req,
    { withCredentials: true },
  )
  return unwrap(data)
}

export async function logout(refreshToken: string): Promise<void> {
  await identityClient.post(`${BASE}/logout`, { refreshToken })
}

// ── Step-up (§8): re-verify a fresh OTP for a high/critical action ────────────

/**
 * The current user's own contact value for `identifierType`, read from the
 * decoded access token (the `email` / `phone` claims). Throws when the chosen
 * channel isn't on file so the caller can surface a clear message instead of
 * POSTing an empty identifier.
 */
export function selfIdentifier(identifierType: StepUpIdentifierType): string {
  const { user } = useAuthStore.getState()
  const value = identifierType === 'email' ? user?.email : user?.phone
  if (!value) throw new Error(`No ${identifierType} on file for your account.`)
  return value
}

/**
 * Send a fresh sensitive-action OTP to the user's OWN phone/email. Anonymous
 * endpoint, but we resolve the identifier from the caller's token so the code
 * lands on their registered contact. `_skipAuthRetry` keeps a rate-limit/validation
 * failure from tripping the 401 refresh-and-retry interceptor.
 */
export async function stepUpSend({
  identifierType,
}: {
  identifierType: StepUpIdentifierType
}): Promise<OtpSentResponse> {
  const identifier = selfIdentifier(identifierType)
  const { data } = await identityClient.post<ApiResponse<OtpSentResponse>>(
    `${BASE}/otp/send`,
    { identifier, identifierType, purpose: 'sensitive_action' },
    SKIP_AUTH_RETRY,
  )
  return unwrap(data)
}

/**
 * Verify the OTP and receive an UPGRADED access token (no refresh token). The
 * identifier is derived server-side from the caller's token — only the type +
 * code travel. `_skipAuthRetry` is essential here: a wrong/expired code returns
 * 401, and without the flag the interceptor would silently refresh and re-submit
 * the same code, consuming a second backend OTP attempt.
 */
export async function stepUpVerify({
  identifierType,
  code,
}: {
  identifierType: StepUpIdentifierType
  code: string
}): Promise<StepUpTokenResponse> {
  const { data } = await identityClient.post<ApiResponse<StepUpTokenResponse>>(
    `${BASE}/step-up/verify`,
    { identifierType, code },
    SKIP_AUTH_RETRY,
  )
  return unwrap(data)
}
