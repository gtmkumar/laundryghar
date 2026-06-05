/**
 * TanStack Query hooks for the public Engagement / CMS endpoints.
 * All calls are anonymous — no auth token required.
 * Data is long-lived CMS content; staleTime is 10 minutes.
 */
import { Platform } from 'react-native';
import { useQuery } from '@tanstack/react-query';
import { getOnboardingSlides, getHomeBanners, getAppConfig } from '@/api/engagement';

// ---------------------------------------------------------------------------
// Query keys
// ---------------------------------------------------------------------------
export const engagementKeys = {
  onboardingSlides: (appType: 'customer' | 'rider') =>
    ['engagement', 'onboarding-slides', appType] as const,
  banners: (placement: string) =>
    ['engagement', 'banners', placement] as const,
  appConfig: (platform: 'ios' | 'android') =>
    ['engagement', 'app-config', platform] as const,
} as const;

const STALE_10_MIN = 10 * 60 * 1_000;

// ---------------------------------------------------------------------------
// useOnboardingSlides
// ---------------------------------------------------------------------------

export function useOnboardingSlides(appType: 'customer' | 'rider') {
  return useQuery({
    queryKey: engagementKeys.onboardingSlides(appType),
    queryFn:  () => getOnboardingSlides(appType),
    staleTime: STALE_10_MIN,
    // Keep previous data while refetching so the carousel never flashes empty
    placeholderData: (prev) => prev,
  });
}

// ---------------------------------------------------------------------------
// useHomeBanners
// ---------------------------------------------------------------------------

export function useHomeBanners(placement: string) {
  return useQuery({
    queryKey: engagementKeys.banners(placement),
    queryFn:  () => getHomeBanners(placement),
    staleTime: STALE_10_MIN,
    placeholderData: (prev) => prev,
  });
}

// ---------------------------------------------------------------------------
// useAppConfig
// Resolves the platform at call time so the hook is usable in the root layout.
// ---------------------------------------------------------------------------

export function useAppConfig() {
  const platform = Platform.OS === 'ios' ? 'ios' : 'android';
  return useQuery({
    queryKey: engagementKeys.appConfig(platform),
    queryFn:  () => getAppConfig(platform),
    staleTime: STALE_10_MIN,
    // Never throw — app-config is advisory; a failure must not block the app
    retry: 1,
  });
}
