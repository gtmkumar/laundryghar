/**
 * Tests for src/lib/format.ts — pure display-formatting helpers.
 *
 * Coverage:
 *   - pluralSuffix: 0/1/many ("1 tasks waiting" regression)
 *   - humanizeVehicleType: known enum values + snake_case fallback
 *   - formatPhone: E.164 +91, bare 10-digit, passthrough for unknown shapes
 */

import { pluralSuffix, humanizeVehicleType, formatPhone } from '../lib/format';

// ---------------------------------------------------------------------------
// pluralSuffix
// ---------------------------------------------------------------------------

describe('pluralSuffix', () => {
  test('1 → no suffix (regression: "1 tasks waiting")', () => {
    expect(pluralSuffix(1)).toBe('');
  });

  test('0 → "s"', () => {
    expect(pluralSuffix(0)).toBe('s');
  });

  test('many → "s"', () => {
    expect(pluralSuffix(2)).toBe('s');
    expect(pluralSuffix(11)).toBe('s');
  });
});

// ---------------------------------------------------------------------------
// humanizeVehicleType
// ---------------------------------------------------------------------------

describe('humanizeVehicleType', () => {
  test('two_wheeler → Two-wheeler', () => {
    expect(humanizeVehicleType('two_wheeler')).toBe('Two-wheeler');
  });

  test('known values map to hyphenated labels', () => {
    expect(humanizeVehicleType('three_wheeler')).toBe('Three-wheeler');
    expect(humanizeVehicleType('four_wheeler')).toBe('Four-wheeler');
    expect(humanizeVehicleType('van')).toBe('Van');
  });

  test('is case-insensitive for known values', () => {
    expect(humanizeVehicleType('TWO_WHEELER')).toBe('Two-wheeler');
  });

  test('unknown snake_case degrades to sentence case', () => {
    expect(humanizeVehicleType('e_rickshaw')).toBe('E rickshaw');
  });

  test('null/undefined/empty → empty string', () => {
    expect(humanizeVehicleType(null)).toBe('');
    expect(humanizeVehicleType(undefined)).toBe('');
    expect(humanizeVehicleType('')).toBe('');
  });
});

// ---------------------------------------------------------------------------
// formatPhone
// ---------------------------------------------------------------------------

describe('formatPhone', () => {
  test('E.164 +91 number → "+91 98765 43210"', () => {
    expect(formatPhone('+919876543210')).toBe('+91 98765 43210');
  });

  test('bare 10-digit number → "+91 98765 43210"', () => {
    expect(formatPhone('9876543210')).toBe('+91 98765 43210');
  });

  test('already spaced/dashed input is normalised', () => {
    expect(formatPhone('98765-43210')).toBe('+91 98765 43210');
  });

  test('non-Indian / unexpected shapes pass through unchanged', () => {
    expect(formatPhone('+4420123456')).toBe('+4420123456');
    expect(formatPhone('12345')).toBe('12345');
  });

  test('null/undefined → empty string', () => {
    expect(formatPhone(null)).toBe('');
    expect(formatPhone(undefined)).toBe('');
  });
});
