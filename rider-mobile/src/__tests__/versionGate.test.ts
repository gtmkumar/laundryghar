/**
 * Tests for rider-mobile/src/lib/versionGate.ts
 *
 * The rider versionGate module is identical in logic to the customer version;
 * the key difference is that the rider DB has NO rows seeded yet —
 * so the primary regression is "empty configRows → none, never throws."
 *
 * Coverage mirrors customer-mobile/src/__tests__/versionGate.test.ts but is
 * kept as a separate file because:
 *   1. The rider app has its own module that could diverge.
 *   2. The seeded-DB regression is rider-specific.
 *
 * No UI strings tested. No screen/navigation imports.
 */

import Constants from 'expo-constants';
import { semverGt, evaluateVersionGate } from '../lib/versionGate';
import type { MobileAppConfigDto } from '../types/api';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeRow(configValue: string, extras: Partial<MobileAppConfigDto> = {}): MobileAppConfigDto {
  return {
    id: 'row-1',
    brandId: 'brand-1',
    appType: 'rider',
    platform: 'android',
    configKey: 'app_settings',
    configValue,
    isForceUpdate: false,
    isActive: true,
    status: 'active',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...extras,
  };
}

function setCurrentVersion(v: string) {
  (Constants as { expoConfig: { version: string } }).expoConfig.version = v;
}

afterEach(() => setCurrentVersion('1.0.0'));

// ---------------------------------------------------------------------------
// semverGt (rider copy)
// ---------------------------------------------------------------------------

describe('semverGt (rider)', () => {
  test('2.0.0 > 1.0.0', () => expect(semverGt('2.0.0', '1.0.0')).toBe(true));
  test('1.0.0 == 1.0.0 → false', () => expect(semverGt('1.0.0', '1.0.0')).toBe(false));
  test('0.9.0 < 1.0.0 → false', () => expect(semverGt('0.9.0', '1.0.0')).toBe(false));
  test('major beats minor: 2.0.0 > 1.99.99', () =>
    expect(semverGt('2.0.0', '1.99.99')).toBe(true));
  test('malformed → treated as 0.0.0', () =>
    expect(semverGt('abc', '0.0.1')).toBe(false));
  test('empty string → treated as 0.0.0', () =>
    expect(semverGt('', '0.0.1')).toBe(false));
});

// ---------------------------------------------------------------------------
// evaluateVersionGate — rider-specific scenarios
// ---------------------------------------------------------------------------

describe('evaluateVersionGate (rider)', () => {
  // ── Primary rider regression: no DB rows yet → none ─────────────────────

  test('null → none (no rows in rider DB)', () =>
    expect(evaluateVersionGate(null)).toEqual({ kind: 'none' }));

  test('undefined → none', () =>
    expect(evaluateVersionGate(undefined)).toEqual({ kind: 'none' }));

  test('empty array → none', () =>
    expect(evaluateVersionGate([])).toEqual({ kind: 'none' }));

  test('rows without app_settings configKey → none', () => {
    const rows = [makeRow('{}', { configKey: 'feature_flags' })];
    expect(evaluateVersionGate(rows)).toEqual({ kind: 'none' });
  });

  // ── Malformed JSON should not propagate ──────────────────────────────────

  test('malformed JSON → none, never throws', () => {
    const rows = [makeRow('!!!not-json!!!')];
    expect(() => evaluateVersionGate(rows)).not.toThrow();
    expect(evaluateVersionGate(rows)).toEqual({ kind: 'none' });
  });

  // ── Force gate ────────────────────────────────────────────────────────────

  test('force_update_version > current → force', () => {
    const rows = [makeRow(JSON.stringify({ force_update_version: '2.0.0' }))];
    expect(evaluateVersionGate(rows).kind).toBe('force');
  });

  test('force gate carries store_url', () => {
    const url = 'https://play.google.com/store/apps/laundryghar-rider';
    const rows = [makeRow(JSON.stringify({ force_update_version: '2.0.0', store_url: url }))];
    const result = evaluateVersionGate(rows);
    if (result.kind === 'force') {
      expect(result.storeUrl).toBe(url);
    } else {
      fail('Expected force gate');
    }
  });

  // ── Soft gate ─────────────────────────────────────────────────────────────

  test('min_version > current, no force → soft', () => {
    const rows = [makeRow(JSON.stringify({ min_version: '1.5.0' }))];
    expect(evaluateVersionGate(rows)).toEqual({ kind: 'soft' });
  });

  // ── No gate ───────────────────────────────────────────────────────────────

  test('force=0.9.0 < current=1.0.0 → none', () => {
    const rows = [makeRow(JSON.stringify({ force_update_version: '0.9.0', min_version: '0.8.0' }))];
    expect(evaluateVersionGate(rows)).toEqual({ kind: 'none' });
  });

  test('force=1.0.0 == current → none (not strictly greater)', () => {
    const rows = [makeRow(JSON.stringify({ force_update_version: '1.0.0' }))];
    expect(evaluateVersionGate(rows)).toEqual({ kind: 'none' });
  });

  // ── Whitespace-only versions are ignored ─────────────────────────────────

  test('whitespace-only version strings → none', () => {
    const rows = [makeRow(JSON.stringify({ force_update_version: '  ', min_version: ' ' }))];
    expect(evaluateVersionGate(rows)).toEqual({ kind: 'none' });
  });
});
