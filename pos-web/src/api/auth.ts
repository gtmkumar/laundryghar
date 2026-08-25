import { identityClient, unwrap } from './client'
import type { ApiResponse, TokenResponse, PasswordLoginRequest } from '@/types/api'

const BASE = '/api/v1/auth'

export async function passwordLogin(req: PasswordLoginRequest): Promise<TokenResponse> {
  const { data } = await identityClient.post<ApiResponse<TokenResponse>>(
    `${BASE}/password/login`,
    req,
  )
  return unwrap(data)
}

/**
 * "Sign in with Google" — exchanges a Google ID token (from Google Identity Services)
 * for a staff session. The server verifies the token against Google's JWKS and requires
 * the verified email to already belong to a provisioned user; it never creates accounts.
 */
export async function googleLogin(idToken: string): Promise<TokenResponse> {
  const { data } = await identityClient.post<ApiResponse<TokenResponse>>(
    `${BASE}/google`,
    { idToken },
  )
  return unwrap(data)
}

export async function refreshTokens(req: { refreshToken: string }): Promise<TokenResponse> {
  const { data } = await identityClient.post<ApiResponse<TokenResponse>>(
    `${BASE}/refresh`,
    req,
  )
  return unwrap(data)
}

export async function logout(refreshToken: string): Promise<void> {
  await identityClient.post(`${BASE}/logout`, { refreshToken })
}
