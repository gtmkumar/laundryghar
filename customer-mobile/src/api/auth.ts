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
  PhoneLinkedResponse,
  SingleResponse,
} from '@/types/api';

/**
 * Best user-facing text from an API envelope.
 *
 * Field-level errors come first: on a 422 the backend's FluentValidation failures land in
 * `errorMessage` as {field: [messages]}, while `responseMessage` is only the generic
 * "One or more validation errors occurred" wrapper. Showing the wrapper throws away the
 * one thing the customer needs — e.g. "That PIN is too easy to guess."
 */
export function envelopeMessage(envelope: SingleResponse<unknown> | undefined): string | null {
  const message = envelope?.message;
  if (!message) return null;

  const fieldErrors = message.errorMessage;
  if (fieldErrors && typeof fieldErrors === 'object') {
    const flat = Object.values(fieldErrors)
      .flat()
      .filter((m): m is string => typeof m === 'string' && m.length > 0);
    if (flat.length) return flat.join(' ');
  }

  return message.responseMessage ?? null;
}

/**
 * Turns an axios/envelope failure into a user-facing Error, preferring the server's own
 * message. Every auth call needs this, and hand-rolling it per call is how inconsistent
 * error copy creeps in.
 */
function toUserError(err: unknown, fallback: string): Error {
  if (err instanceof Error && !axios.isAxiosError(err)) return err;

  if (axios.isAxiosError(err)) {
    if (err.response?.status === 429) {
      return new Error('Too many attempts. Please wait a moment and try again.');
    }
    const serverMessage = envelopeMessage(err.response?.data as SingleResponse<unknown>);
    return new Error(serverMessage ?? fallback);
  }
  return new Error(fallback);
}

/** Unwraps the API envelope, raising the server's message when the call did not succeed. */
function unwrap<T>(envelope: SingleResponse<T>, fallback: string): T {
  if (!envelope.status || !envelope.data) {
    throw new Error(envelopeMessage(envelope) ?? fallback);
  }
  return envelope.data;
}

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
    return unwrap(res.data, 'OTP verification failed');
  } catch (err: unknown) {
    // CUST-BUG-04: toUserError surfaces the rate-limit message ahead of the fallback.
    throw toUserError(err, 'That code is incorrect or has expired. Please try again.');
  }
}

// ---------------------------------------------------------------------------
// POST /api/v1/customer/auth/google
// Signs in (or signs up) with a Google ID token obtained via expo-auth-session.
// The server verifies the token against Google's JWKS — the client is not trusted.
// ---------------------------------------------------------------------------
export async function signInWithGoogle(
  idToken: string,
  brandCode?: string,
): Promise<CustomerTokenResponse> {
  try {
    const res = await identityClient.post<SingleResponse<CustomerTokenResponse>>(
      '/customer/auth/google',
      { idToken, brandCode: brandCode ?? CONFIG.defaultBrandCode },
    );
    return unwrap(res.data, 'Google sign-in failed');
  } catch (err: unknown) {
    throw toUserError(err, 'Google sign-in failed. Please try again.');
  }
}

// ---------------------------------------------------------------------------
// POST /api/v1/customer/auth/phone/link/send  (authenticated)
// Step 1 of attaching a phone number to a Google-created account.
// ---------------------------------------------------------------------------
export async function sendPhoneLinkOtp(phone: string): Promise<void> {
  try {
    await identityClient.post('/customer/auth/phone/link/send', { phone });
  } catch (err: unknown) {
    throw toUserError(err, 'Could not send the code. Please try again.');
  }
}

// ---------------------------------------------------------------------------
// POST /api/v1/customer/auth/phone/link/verify  (authenticated)
// Step 2 — writes the verified number onto the caller's own account.
// ---------------------------------------------------------------------------
export async function verifyPhoneLinkOtp(
  phone: string,
  code: string,
): Promise<PhoneLinkedResponse> {
  try {
    const res = await identityClient.post<SingleResponse<PhoneLinkedResponse>>(
      '/customer/auth/phone/link/verify',
      { phone, code },
    );
    return unwrap(res.data, 'Could not verify that code');
  } catch (err: unknown) {
    throw toUserError(err, 'That code is incorrect or has expired. Please try again.');
  }
}

// ---------------------------------------------------------------------------
// POST /api/v1/customer/auth/pin  (authenticated) — set or replace the unlock PIN
// ---------------------------------------------------------------------------
export async function setPin(pin: string): Promise<void> {
  try {
    await identityClient.post('/customer/auth/pin', { pin });
  } catch (err: unknown) {
    throw toUserError(err, 'Could not save your PIN. Please try again.');
  }
}

// ---------------------------------------------------------------------------
// POST /api/v1/customer/auth/pin/verify — returning-user unlock, no OTP needed
// ---------------------------------------------------------------------------
export async function verifyPin(
  identifier: string,
  pin: string,
  brandCode?: string,
): Promise<CustomerTokenResponse> {
  try {
    const res = await identityClient.post<SingleResponse<CustomerTokenResponse>>(
      '/customer/auth/pin/verify',
      { identifier, pin, brandCode: brandCode ?? CONFIG.defaultBrandCode },
    );
    return unwrap(res.data, 'Incorrect PIN');
  } catch (err: unknown) {
    throw toUserError(err, 'Incorrect PIN. Please try again.');
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
  return unwrap(res.data, 'Failed to fetch profile');
}
