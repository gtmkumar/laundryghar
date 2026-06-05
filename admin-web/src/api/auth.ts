import { identityClient, unwrap } from './client'
import type { ApiResponse, TokenResponse, PasswordLoginRequest, RefreshTokenRequest } from '@/types/api'

const BASE = '/api/v1/auth'

export async function passwordLogin(req: PasswordLoginRequest): Promise<TokenResponse> {
  const { data } = await identityClient.post<ApiResponse<TokenResponse>>(
    `${BASE}/password/login`,
    req,
  )
  return unwrap(data)
}

export async function refreshTokens(req: RefreshTokenRequest): Promise<TokenResponse> {
  const { data } = await identityClient.post<ApiResponse<TokenResponse>>(
    `${BASE}/refresh`,
    req,
  )
  return unwrap(data)
}

export async function logout(refreshToken: string): Promise<void> {
  await identityClient.post(`${BASE}/logout`, { refreshToken })
}
