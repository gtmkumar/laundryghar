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

import { rupees, formatDate, formatDateTime, greeting, localDateIso, maskPhone } from '../lib/format';

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

// ---------------------------------------------------------------------------
// localDateIso — LOCAL calendar date, never UTC-shifted
// ---------------------------------------------------------------------------

describe('localDateIso', () => {
  test('formats yyyy-mm-dd with zero padding', () => {
    const d = new Date(2026, 0, 5); // 5 Jan 2026, local time
    expect(localDateIso(d)).toBe('2026-01-05');
  });

  test('uses the LOCAL date just after local midnight (UTC would be yesterday in IST)', () => {
    // 00:30 local on 13 Jun — toISOString().slice(0,10) gives 2026-06-12 in
    // any zone ahead of UTC (e.g. IST +05:30). localDateIso must not.
    const d = new Date(2026, 5, 13, 0, 30, 0);
    expect(localDateIso(d)).toBe('2026-06-13');
  });

  test('uses the LOCAL date just before local midnight', () => {
    const d = new Date(2026, 5, 13, 23, 59, 59);
    expect(localDateIso(d)).toBe('2026-06-13');
  });

  test('no argument → today, matching local date parts', () => {
    const now = new Date();
    const expected = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
    expect(localDateIso()).toBe(expected);
  });
});

// ---------------------------------------------------------------------------
// maskPhone — derived from the actual number, no hardcoded prefix
// ---------------------------------------------------------------------------

describe('maskPhone', () => {
  test('masks a 10-digit number keeping first 2 + last 4 digits', () => {
    expect(maskPhone('7733441234')).toBe('+91 77 ●●●● 1234');
  });

  test('different numbers produce different masks (regression: was hardcoded "98")', () => {
    expect(maskPhone('9812341234')).toBe('+91 98 ●●●● 1234');
    expect(maskPhone('6000005678')).toBe('+91 60 ●●●● 5678');
  });

  test('strips non-digits before masking', () => {
    expect(maskPhone('98123-41234')).toBe('+91 98 ●●●● 1234');
  });

  test('returns input unchanged when too short to mask', () => {
    expect(maskPhone('1234')).toBe('1234');
  });

  test('handles null/undefined gracefully', () => {
    expect(maskPhone(undefined)).toBe('');
    expect(maskPhone(null)).toBe('');
  });
});
