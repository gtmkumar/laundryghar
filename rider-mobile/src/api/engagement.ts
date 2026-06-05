/**
 * Engagement service API — public / anonymous endpoints.
 *
 * These calls run BEFORE login, so:
 *   - No Bearer token is attached.
 *   - No 401 refresh logic is needed.
 *   - Brand is passed via the `brandCode` query param (resolved server-side).
 *
 * Envelope shape: { status: boolean, data: T[] }  →  ListResponse<T>
 */
import axios from 'axios';
import { CONFIG } from '@/constants/config';
import type {
  ListResponse,
  MobileAppConfigDto,
  OnboardingSlideDto,
} from '@/types/api';

// ---------------------------------------------------------------------------
// Anonymous axios instance — no auth interceptors
// ---------------------------------------------------------------------------

const engagementClient = axios.create({
  baseURL: `${CONFIG.engagementApiUrl}/api/v1`,
  timeout: 15_000,
  headers: { 'Content-Type': 'application/json' },
});

// ---------------------------------------------------------------------------
// Brand helper — always send brandCode so the server can resolve to a brand UUID
// ---------------------------------------------------------------------------

function brandParams(extra?: Record<string, string>): Record<string, string> {
  return { brandCode: CONFIG.defaultBrandCode, ...extra };
}

// ---------------------------------------------------------------------------
// GET /api/v1/public/onboarding-slides?appType=rider
// ---------------------------------------------------------------------------

export async function getOnboardingSlides(
  appType: 'customer' | 'rider',
): Promise<OnboardingSlideDto[]> {
  const res = await engagementClient.get<ListResponse<OnboardingSlideDto>>(
    '/public/onboarding-slides',
    { params: brandParams({ appType }) },
  );
  const envelope = res.data;
  if (!envelope.status) {
    throw new Error(envelope.message?.responseMessage ?? 'Failed to load onboarding slides');
  }
  return envelope.data ?? [];
}

// ---------------------------------------------------------------------------
// GET /api/v1/public/app-config?platform=ios|android
// ---------------------------------------------------------------------------

export async function getAppConfig(
  platform: 'ios' | 'android',
): Promise<MobileAppConfigDto[]> {
  const res = await engagementClient.get<ListResponse<MobileAppConfigDto>>(
    '/public/app-config',
    { params: brandParams({ platform }) },
  );
  const envelope = res.data;
  if (!envelope.status) {
    throw new Error(envelope.message?.responseMessage ?? 'Failed to load app config');
  }
  return envelope.data ?? [];
}
