/**
 * Rider auth API — maps to AuthEndpoints.cs (shared system auth)
 * Endpoint prefix: {Identity}/api/v1/auth/
 *
 * Riders are SYSTEM users (user_type='rider') — they log in with a
 * password, not OTP.  The endpoint is POST /api/v1/auth/password/login.
 */
import { identityClient } from '@/api/client';
import type {
  PasswordLoginRequest,
  SingleResponse,
  TokenResponse,
} from '@/types/api';

// ---------------------------------------------------------------------------
// POST /api/v1/auth/password/login
// ---------------------------------------------------------------------------
export async function passwordLogin(
  identifier: string,
  password: string,
): Promise<TokenResponse> {
  const payload: PasswordLoginRequest = { identifier, password };
  const res = await identityClient.post<SingleResponse<TokenResponse>>(
    '/auth/password/login',
    payload,
  );
  const envelope = res.data;
  if (!envelope.status || !envelope.data) {
    throw new Error(envelope.message?.responseMessage ?? 'Login failed');
  }
  return envelope.data;
}

// ---------------------------------------------------------------------------
// POST /api/v1/auth/refresh
// Called from the axios interceptor — returns new accessToken string only.
// ---------------------------------------------------------------------------
export async function refreshAccessToken(refreshToken: string): Promise<string> {
  const res = await identityClient.post<SingleResponse<TokenResponse>>(
    '/auth/refresh',
    { refreshToken },
  );
  const envelope = res.data;
  if (!envelope.status || !envelope.data?.accessToken) {
    throw new Error('Token refresh failed');
  }
  return envelope.data.accessToken;
}

// ---------------------------------------------------------------------------
// POST /api/v1/auth/logout
// ---------------------------------------------------------------------------
export async function logout(refreshToken: string): Promise<void> {
  await identityClient.post('/auth/logout', { refreshToken });
}
