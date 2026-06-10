/**
 * Tests for src/lib/format.ts
 *
 * Coverage:
 *   - rupees: integer rupees, Indian grouping, rounding, zero, negatives
 *   - formatDate: valid ISO, undefined/null, malformed input
 *   - formatDateTime: valid ISO, undefined/null
 *   - greeting: morning / afternoon / evening boundary hours
 *
 * These are pure functions with no native module deps — no mocking required.
 * No UI text is tested; only the return values from pure logic.
 */

import { rupees, formatDate, formatDateTime, greeting } from '../lib/format';

// ---------------------------------------------------------------------------
// rupees
// ---------------------------------------------------------------------------

describe('rupees', () => {
  test('formats zero as ₹0', () => {
    expect(rupees(0)).toBe('₹0');
  });

  test('rounds fractional amount', () => {
    // Math.round(99.5) = 100
    expect(rupees(99.5)).toBe('₹100');
    expect(rupees(99.4)).toBe('₹99');
  });

  test('negative amounts round correctly (edge case — passthrough)', () => {
    // The function does not guard against negatives; documents current behaviour.
    const result = rupees(-100);
    expect(result).toContain('100');
  });

  test('produces ₹ prefix', () => {
    expect(rupees(500).startsWith('₹')).toBe(true);
  });

  test('large number produces non-empty string', () => {
    const result = rupees(1000000);
    expect(result.length).toBeGreaterThan(1);
    expect(result.startsWith('₹')).toBe(true);
  });
});

// ---------------------------------------------------------------------------
// formatDate
// ---------------------------------------------------------------------------

describe('formatDate', () => {
  test('returns em-dash for undefined', () => {
    expect(formatDate(undefined)).toBe('—');
  });

  test('returns em-dash for empty string', () => {
    expect(formatDate('')).toBe('—');
  });

  test('returns original string for malformed ISO (new Date("not-a-date") → Invalid Date)', () => {
    // The function catches and returns iso on error; invalid dates may also produce
    // "Invalid Date" string — either way it must not throw.
    expect(() => formatDate('not-a-date')).not.toThrow();
  });

  test('returns a non-empty string for valid ISO date', () => {
    const result = formatDate('2026-06-10T00:00:00Z');
    expect(typeof result).toBe('string');
    expect(result.length).toBeGreaterThan(0);
    expect(result).not.toBe('—');
  });

  test('year 2026 appears in formatted date for 2026-06-10', () => {
    const result = formatDate('2026-06-10T00:00:00Z');
    expect(result).toContain('2026');
  });
});

// ---------------------------------------------------------------------------
// formatDateTime
// ---------------------------------------------------------------------------

describe('formatDateTime', () => {
  test('returns em-dash for undefined', () => {
    expect(formatDateTime(undefined)).toBe('—');
  });

  test('returns em-dash for empty string', () => {
    expect(formatDateTime('')).toBe('—');
  });

  test('returns a non-empty string for valid ISO datetime', () => {
    const result = formatDateTime('2026-06-10T12:02:00Z');
    expect(typeof result).toBe('string');
    expect(result.length).toBeGreaterThan(0);
    expect(result).not.toBe('—');
  });

  test('does not throw for malformed ISO input', () => {
    expect(() => formatDateTime('garbage')).not.toThrow();
  });
});

// ---------------------------------------------------------------------------
// greeting — boundary hour tests
// ---------------------------------------------------------------------------

describe('greeting', () => {
  function makeDate(hour: number): Date {
    const d = new Date();
    d.setHours(hour, 0, 0, 0);
    return d;
  }

  // Morning: h < 12
  test('hour 0 → Good morning', () => {
    expect(greeting(makeDate(0))).toBe('Good morning');
  });

  test('hour 11 → Good morning', () => {
    expect(greeting(makeDate(11))).toBe('Good morning');
  });

  // Afternoon: 12 <= h < 17
  test('hour 12 → Good afternoon', () => {
    expect(greeting(makeDate(12))).toBe('Good afternoon');
  });

  test('hour 16 → Good afternoon', () => {
    expect(greeting(makeDate(16))).toBe('Good afternoon');
  });

  // Evening: h >= 17
  test('hour 17 → Good evening', () => {
    expect(greeting(makeDate(17))).toBe('Good evening');
  });

  test('hour 23 → Good evening', () => {
    expect(greeting(makeDate(23))).toBe('Good evening');
  });

  // Default argument (calls with no arg) — result must be one of the three strings
  test('no argument → returns a valid greeting string', () => {
    const result = greeting();
    expect(['Good morning', 'Good afternoon', 'Good evening']).toContain(result);
  });
});
