/**
 * Customer auth API — maps to CustomerAuthEndpoints.cs
 * Endpoint prefix: {Identity}/api/v1/customer/auth/
 */
import axios from 'axios';
import { identityClient } from '@/api/client';
import { CONFIG } from '@/constants/config';
import type {
  CustomerMeResponse,
  CustomerTokenResponse,
  OtpSendRequest,
  SingleResponse,
} from '@/types/api';

// ---------------------------------------------------------------------------
// POST /api/v1/customer/auth/otp/send
// ---------------------------------------------------------------------------
export async function sendOtp(phone: string, brandCode?: string): Promise<void> {
  const payload: OtpSendRequest = {
    phone,
    brandCode: brandCode ?? CONFIG.defaultBrandCode,
  };
  await identityClient.post<SingleResponse<{ message?: string }>>(
    '/customer/auth/otp/send',
    payload,
  );
}

// ---------------------------------------------------------------------------
// POST /api/v1/customer/auth/otp/verify
// ---------------------------------------------------------------------------
export async function verifyOtp(
  phone: string,
  code: string,
  brandCode?: string,
): Promise<CustomerTokenResponse> {
  try {
    const res = await identityClient.post<SingleResponse<CustomerTokenResponse>>(
      '/customer/auth/otp/verify',
      { phone, code, brandCode: brandCode ?? CONFIG.defaultBrandCode },
    );
    const envelope = res.data;
    if (!envelope.status || !envelope.data) {
      throw new Error(envelope.message?.responseMessage ?? 'OTP verification failed');
    }
    return envelope.data;
  } catch (err: unknown) {
    // Re-throw envelope errors as-is (already a friendly Error from above)
    if (err instanceof Error && !(axios.isAxiosError(err))) {
      throw err;
    }
    // For axios HTTP/network errors, extract the server's responseMessage when
    // present on the error body, otherwise use a friendly fallback.
    if (axios.isAxiosError(err)) {
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
// POST /api/v1/customer/auth/refresh
// Called from the axios interceptor — takes raw refreshToken, returns new accessToken
// ---------------------------------------------------------------------------
export async function refreshAccessToken(refreshToken: string): Promise<string> {
  // We need a clean axios call (not going through the interceptor-wrapped instance
  // because that would recursively trigger 401 handling). Use the base instance directly.
  const res = await identityClient.post<SingleResponse<CustomerTokenResponse>>(
    '/customer/auth/refresh',
    { refreshToken },
  );
  const envelope = res.data;
  if (!envelope.status || !envelope.data?.accessToken) {
    throw new Error('Token refresh failed');
  }
  // The caller (auth store) must persist both tokens
  return envelope.data.accessToken;
}

// ---------------------------------------------------------------------------
// POST /api/v1/customer/auth/logout
// ---------------------------------------------------------------------------
export async function logout(refreshToken: string): Promise<void> {
  await identityClient.post('/customer/auth/logout', { refreshToken });
}

// ---------------------------------------------------------------------------
// GET /api/v1/customer/auth/me
// ---------------------------------------------------------------------------
export async function getMe(): Promise<CustomerMeResponse> {
  const res = await identityClient.get<SingleResponse<CustomerMeResponse>>(
    '/customer/auth/me',
  );
  const envelope = res.data;
  if (!envelope.status || !envelope.data) {
    throw new Error(envelope.message?.responseMessage ?? 'Failed to fetch profile');
  }
  return envelope.data;
}
