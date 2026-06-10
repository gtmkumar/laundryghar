/**
 * Tests for src/lib/versionGate.ts
 *
 * Coverage:
 *   - semverGt: ordering, equality, major/minor/patch precedence, malformed inputs
 *   - evaluateVersionGate: none / force / soft paths, missing config, malformed JSON,
 *     empty rows, store_url propagation, defensive null handling
 *
 * Screen-text independence: no UI strings are asserted — logic only.
 * expo-constants is mocked via moduleNameMapper (src/__mocks__/expo-constants.ts)
 * and reports version "1.0.0" by default. Tests that need a different current
 * version mutate Constants.expoConfig.version inline and restore it in afterEach.
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
    appType: 'customer',
    platform: 'ios',
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
  // The mock exposes expoConfig as a plain object — mutate it for the duration of a test.
  (Constants as { expoConfig: { version: string } }).expoConfig.version = v;
}

// ---------------------------------------------------------------------------
// semverGt
// ---------------------------------------------------------------------------

describe('semverGt', () => {
  // Strict greater-than
  test('2.0.0 > 1.0.0', () => expect(semverGt('2.0.0', '1.0.0')).toBe(true));
  test('1.1.0 > 1.0.0', () => expect(semverGt('1.1.0', '1.0.0')).toBe(true));
  test('1.0.1 > 1.0.0', () => expect(semverGt('1.0.1', '1.0.0')).toBe(true));

  // Equal — must be false (not >=)
  test('1.0.0 == 1.0.0 → false', () => expect(semverGt('1.0.0', '1.0.0')).toBe(false));

  // Less-than
  test('1.0.0 < 2.0.0 → false', () => expect(semverGt('1.0.0', '2.0.0')).toBe(false));
  test('1.0.0 < 1.1.0 → false', () => expect(semverGt('1.0.0', '1.1.0')).toBe(false));
  test('1.0.0 < 1.0.1 → false', () => expect(semverGt('1.0.0', '1.0.1')).toBe(false));

  // Major takes precedence
  test('2.0.0 > 1.9.9 — major beats minor+patch', () =>
    expect(semverGt('2.0.0', '1.9.9')).toBe(true));

  // Minor takes precedence over patch
  test('1.10.0 > 1.9.9 — minor beats patch', () =>
    expect(semverGt('1.10.0', '1.9.9')).toBe(true));

  // Malformed / partial version strings — parseInt falls back to 0 via `|| 0`
  test('malformed "abc.def.ghi" treated as 0.0.0', () =>
    expect(semverGt('abc.def.ghi', '0.0.1')).toBe(false));

  test('empty string treated as 0.0.0', () =>
    expect(semverGt('', '0.0.1')).toBe(false));

  test('partial "2" (no dots) resolves to 2.0.0 > 1.0.0', () =>
    expect(semverGt('2', '1.0.0')).toBe(true));

  // Leading/trailing whitespace tolerance (trimmed inside parse)
  test('whitespace padded " 2.0.0 " > "1.0.0"', () =>
    expect(semverGt(' 2.0.0 ', '1.0.0')).toBe(true));
});

// ---------------------------------------------------------------------------
// evaluateVersionGate
// ---------------------------------------------------------------------------

describe('evaluateVersionGate', () => {
  afterEach(() => {
    // Restore default mock version after each test
    setCurrentVersion('1.0.0');
  });

  // ── Null / empty config → none ──────────────────────────────────────────

  test('null rows → none', () =>
    expect(evaluateVersionGate(null)).toEqual({ kind: 'none' }));

  test('undefined rows → none', () =>
    expect(evaluateVersionGate(undefined)).toEqual({ kind: 'none' }));

  test('empty array → none', () =>
    expect(evaluateVersionGate([])).toEqual({ kind: 'none' }));

  test('rows without app_settings key → none', () => {
    const rows = [makeRow('{}', { configKey: 'other_key' })];
    expect(evaluateVersionGate(rows)).toEqual({ kind: 'none' });
  });

  // ── Malformed config JSON → none (defensive) ────────────────────────────

  test('malformed JSON config → none, never throws', () => {
    const rows = [makeRow('{not valid json}')];
    expect(() => evaluateVersionGate(rows)).not.toThrow();
    expect(evaluateVersionGate(rows)).toEqual({ kind: 'none' });
  });

  test('empty string config value → none', () => {
    const rows = [makeRow('')];
    expect(evaluateVersionGate(rows)).toEqual({ kind: 'none' });
  });

  // ── No gate active — force and min below current version ────────────────

  test('force_update_version < current → none', () => {
    // Current = 1.0.0, force = 0.9.0 → no gate
    const rows = [makeRow(JSON.stringify({ force_update_version: '0.9.0', min_version: '0.8.0' }))];
    expect(evaluateVersionGate(rows)).toEqual({ kind: 'none' });
  });

  test('force and min equal to current → none (not strictly greater)', () => {
    const rows = [makeRow(JSON.stringify({ force_update_version: '1.0.0', min_version: '1.0.0' }))];
    expect(evaluateVersionGate(rows)).toEqual({ kind: 'none' });
  });

  // ── Force update gate ───────────────────────────────────────────────────

  test('force_update_version > current → { kind: force }', () => {
    // Current = 1.0.0, force = 2.0.0
    const rows = [makeRow(JSON.stringify({ force_update_version: '2.0.0' }))];
    expect(evaluateVersionGate(rows)).toEqual({ kind: 'force', storeUrl: undefined });
  });

  test('force gate propagates store_url', () => {
    const storeUrl = 'https://apps.apple.com/app/laundryghar/id123';
    const rows = [makeRow(JSON.stringify({ force_update_version: '2.0.0', store_url: storeUrl }))];
    expect(evaluateVersionGate(rows)).toEqual({ kind: 'force', storeUrl });
  });

  test('force gate: store_url undefined when not in config', () => {
    const rows = [makeRow(JSON.stringify({ force_update_version: '2.0.0' }))];
    const result = evaluateVersionGate(rows);
    expect(result.kind).toBe('force');
    if (result.kind === 'force') {
      expect(result.storeUrl).toBeUndefined();
    }
  });

  test('force gate takes precedence over soft gate', () => {
    // Both force and min are above current — force must win
    const rows = [makeRow(JSON.stringify({ force_update_version: '2.0.0', min_version: '1.5.0' }))];
    expect(evaluateVersionGate(rows).kind).toBe('force');
  });

  // ── Soft gate ───────────────────────────────────────────────────────────

  test('min_version > current, no force → { kind: soft }', () => {
    // Current = 1.0.0, min = 1.1.0 → soft banner
    const rows = [makeRow(JSON.stringify({ min_version: '1.1.0' }))];
    expect(evaluateVersionGate(rows)).toEqual({ kind: 'soft' });
  });

  test('min_version equals current → none (not soft)', () => {
    const rows = [makeRow(JSON.stringify({ min_version: '1.0.0' }))];
    expect(evaluateVersionGate(rows)).toEqual({ kind: 'none' });
  });

  // ── Seeded DB values (regression: force=0.9.0 < current=1.0.0 → no gate) ──

  test('seeded DB state: force=0.9.0, min=1.0.0, app=1.0.0 → none', () => {
    const rows = [makeRow(JSON.stringify({ force_update_version: '0.9.0', min_version: '1.0.0' }))];
    expect(evaluateVersionGate(rows)).toEqual({ kind: 'none' });
  });

  // ── App version fallback: Constants returns undefined → treated as 0.0.0 ──

  test('undefined expoConfig.version falls back to 0.0.0 → force gate triggers', () => {
    (Constants as { expoConfig: { version: string | undefined } }).expoConfig.version = undefined as unknown as string;
    const rows = [makeRow(JSON.stringify({ force_update_version: '0.0.1' }))];
    // 0.0.1 > 0.0.0 → force
    expect(evaluateVersionGate(rows).kind).toBe('force');
  });

  // ── Whitespace-only version strings are treated as absent ──────────────

  test('force_update_version whitespace-only → ignored → none', () => {
    const rows = [makeRow(JSON.stringify({ force_update_version: '   ', min_version: '   ' }))];
    expect(evaluateVersionGate(rows)).toEqual({ kind: 'none' });
  });
});
