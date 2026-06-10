/**
 * Version-gate helpers — customer-mobile.
 *
 * Reads `app_settings` from the MobileAppConfigDto rows fetched at boot and
 * computes what gate (if any) should be shown.
 *
 * Contract (engagement_cms.mobile_app_config → config_key = 'app_settings'):
 *   configValue.min_version           — soft minimum (dismissible banner)
 *   configValue.force_update_version  — hard minimum (blocking modal)
 *   configValue.store_url             — optional store deep-link for forced updates
 *
 * Seeded data as of 2026-06-10:
 *   min_version           = "1.0.0"
 *   force_update_version  = "0.9.0"  ← lower than current → no gate active
 *   store_url             = (not seeded — treated as undefined)
 *
 * NOTE: The rider app_type has no rows in the DB yet. This module is
 * customer-only; see rider-mobile/src/lib/versionGate.ts for the rider copy.
 *
 * Guarded by FEATURES.versionGate. Tolerates missing / malformed config silently.
 */
import Constants from 'expo-constants';
import type { MobileAppConfigDto, AppSettingsConfigValue } from '@/types/api';

// ---------------------------------------------------------------------------
// Semver helper
// ---------------------------------------------------------------------------

/** Returns true when version string `a` is strictly greater than `b`. */
export function semverGt(a: string, b: string): boolean {
  const parse = (v: string) => v.trim().split('.').map((n) => parseInt(n, 10) || 0);
  const [aMaj = 0, aMin = 0, aPat = 0] = parse(a);
  const [bMaj = 0, bMin = 0, bPat = 0] = parse(b);
  if (aMaj !== bMaj) return aMaj > bMaj;
  if (aMin !== bMin) return aMin > bMin;
  return aPat > bPat;
}

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

export type VersionGateResult =
  | { kind: 'none' }
  | { kind: 'force'; storeUrl: string | undefined }
  | { kind: 'soft' };

// ---------------------------------------------------------------------------
// Main export
// ---------------------------------------------------------------------------

/**
 * Evaluate the version gate from a list of MobileAppConfigDto rows.
 *
 * Returns:
 *   { kind: 'none'  }  — no action needed
 *   { kind: 'force' }  — blocking modal; app must update before proceeding
 *   { kind: 'soft'  }  — dismissible banner; app should update
 *
 * Never throws — tolerates null / undefined / malformed config.
 */
export function evaluateVersionGate(
  configRows: MobileAppConfigDto[] | null | undefined,
): VersionGateResult {
  try {
    if (!configRows || configRows.length === 0) return { kind: 'none' };

    const row = configRows.find((r) => r.configKey === 'app_settings');
    if (!row) return { kind: 'none' };

    let settings: AppSettingsConfigValue;
    try {
      settings = JSON.parse(row.configValue) as AppSettingsConfigValue;
    } catch {
      return { kind: 'none' };
    }

    const currentVersion: string =
      (Constants.expoConfig?.version as string | undefined) ?? '0.0.0';

    // Force update gate — hard block
    const forceVersion = settings.force_update_version;
    if (forceVersion && forceVersion.trim() && semverGt(forceVersion, currentVersion)) {
      return { kind: 'force', storeUrl: settings.store_url };
    }

    // Soft minimum — dismissible banner
    const minVersion = settings.min_version;
    if (minVersion && minVersion.trim() && semverGt(minVersion, currentVersion)) {
      return { kind: 'soft' };
    }

    return { kind: 'none' };
  } catch {
    // Defensive — never propagate
    return { kind: 'none' };
  }
}
