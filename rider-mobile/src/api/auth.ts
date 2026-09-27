/**
 * Rider auth API — maps to AuthEndpoints.cs (shared system auth)
 * Endpoint prefix: {Identity}/api/v1/auth/
 *
 * Riders are SYSTEM users (user_type='rider') and sign in with a 6-digit phone OTP:
 * POST /auth/otp/send then POST /auth/otp/verify. That is the only flow the app offers —
 * app/(auth)/login.tsx asks for a phone number and nothing else.
 *
 * This header used to say the opposite ("they log in with a password, not OTP") and described a
 * `passwordLogin` helper that no screen ever called. Audit finding A-7. The helper has been
 * removed rather than left as dead code behind a corrected comment: the backend endpoint still
 * exists and is used by other clients, so a rider password flow can be added back deliberately if
 * it is ever wanted, instead of lingering as something that looks supported and is not.
 */
import axios from 'axios';
import { identityClient } from '@/api/client';
import type {
  OtpSendRequest,
  OtpSentResponse,
  OtpVerifyRequest,
  SingleResponse,
  TokenResponse,
} from '@/types/api';

const PHONE = 'phone';
const LOGIN = 'login';

// ---------------------------------------------------------------------------
// POST /api/v1/auth/otp/send   (purpose="login", identifierType="phone")
// Backend never reveals whether the number exists — always resolves on 2xx.
// ---------------------------------------------------------------------------
export async function sendLoginOtp(phoneE164: string): Promise<OtpSentResponse> {
  const payload: OtpSendRequest = {
    identifier:     phoneE164,
    identifierType: PHONE,
    purpose:        LOGIN,
  };
  const res = await identityClient.post<SingleResponse<OtpSentResponse>>(
    '/auth/otp/send',
    payload,
  );
  const envelope = res.data;
  if (!envelope.status || !envelope.data) {
    throw new Error(envelope.message?.responseMessage ?? 'Could not send OTP');
  }
  return envelope.data;
}

// ---------------------------------------------------------------------------
// POST /api/v1/auth/otp/verify  →  TokenResponse (accessToken + refreshToken)
// ---------------------------------------------------------------------------
export async function verifyLoginOtp(
  phoneE164: string,
  code: string,
): Promise<TokenResponse> {
  const payload: OtpVerifyRequest = {
    identifier:     phoneE164,
    identifierType: PHONE,
    purpose:        LOGIN,
    code,
  };
  try {
    const res = await identityClient.post<SingleResponse<TokenResponse>>(
      '/auth/otp/verify',
      payload,
    );
    const envelope = res.data;
    if (!envelope.status || !envelope.data) {
      throw new Error(envelope.message?.responseMessage ?? 'Invalid or expired code');
    }
    return envelope.data;
  } catch (err: unknown) {
    // Re-throw envelope errors (already a friendly Error from above).
    if (err instanceof Error && !axios.isAxiosError(err)) {
      throw err;
    }
    if (axios.isAxiosError(err)) {
      // RIDER-BUG-01: surface rate-limit message before the generic fallback.
      if (err.response?.status === 429) {
        throw new Error('Too many attempts. Please wait a moment and try again.');
      }
      const serverMessage =
        (err.response?.data as SingleResponse<unknown> | undefined)
          ?.message?.responseMessage;
      throw new Error(
        serverMessage ?? 'That code is incorrect or has expired. Please try again.',
      );
    }
    throw new Error('That code is incorrect or has expired. Please try again.');
  }
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
