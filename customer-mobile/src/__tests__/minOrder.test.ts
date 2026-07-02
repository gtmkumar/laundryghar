/**
 * Tests for src/lib/minOrder.ts (GitHub #23)
 *
 * Coverage:
 *   - formatCurrency: known symbol, unknown code fallback, rounding/grouping
 *   - evaluateMinOrder: no config, null/zero minimum, below/at/above minimum
 *   - parseMinOrderError: raw Axios 422 envelope, ApiError, non-matching errors,
 *     string-array field parsing, computed shortfall fallback
 *
 * Pure logic — no native module deps, no mocking beyond a hand-built ApiError.
 */
import {
  evaluateMinOrder,
  formatCurrency,
  parseMinOrderError,
} from '../lib/minOrder';
import { ApiError } from '../api/client';
import type { CatalogConfigDto } from '../types/api';

const cfg = (min: number | null): CatalogConfigDto => ({
  minOrderValue: min,
  currencyCode: 'INR',
  highValueGarmentThreshold: null,
});

// ---------------------------------------------------------------------------
// formatCurrency
// ---------------------------------------------------------------------------

describe('formatCurrency', () => {
  test('uses the symbol for a known code with Indian grouping', () => {
    expect(formatCurrency(1545, 'INR')).toBe('₹1,545');
    expect(formatCurrency(50, 'USD')).toBe('$50');
  });

  test('rounds to whole units', () => {
    expect(formatCurrency(149.5, 'INR')).toBe('₹150');
  });

  test('falls back to the raw code when unknown', () => {
    expect(formatCurrency(200, 'NPR')).toBe('NPR 200');
  });
});

// ---------------------------------------------------------------------------
// evaluateMinOrder
// ---------------------------------------------------------------------------

describe('evaluateMinOrder', () => {
  test('returns null when config is missing (no restriction)', () => {
    expect(evaluateMinOrder(100, undefined)).toBeNull();
  });

  test('returns null when minimum is null or non-positive', () => {
    expect(evaluateMinOrder(100, cfg(null))).toBeNull();
    expect(evaluateMinOrder(100, cfg(0))).toBeNull();
  });

  test('blocks and computes shortfall when below minimum', () => {
    const gate = evaluateMinOrder(350, cfg(500));
    expect(gate).toEqual({
      blocked: true,
      minOrderValue: 500,
      currencyCode: 'INR',
      shortfall: 150,
    });
  });

  test('does not block at or above minimum, shortfall clamps to 0', () => {
    expect(evaluateMinOrder(500, cfg(500))?.blocked).toBe(false);
    expect(evaluateMinOrder(600, cfg(500))?.blocked).toBe(false);
    expect(evaluateMinOrder(600, cfg(500))?.shortfall).toBe(0);
  });
});

// ---------------------------------------------------------------------------
// parseMinOrderError
// ---------------------------------------------------------------------------

describe('parseMinOrderError', () => {
  test('parses a raw Axios 422 envelope with string-array fields', () => {
    const err = {
      response: {
        status: 422,
        data: {
          status: false,
          message: {
            errorTypeCode: 422,
            errorMessage: {
              code: ['min_order_value_not_met'],
              minimum: ['500'],
              subtotal: ['350'],
              shortfall: ['150'],
            },
            responseMessage: 'Order total is below the minimum of 500. Add 150 more to place this order.',
          },
        },
      },
    };
    expect(parseMinOrderError(err)).toEqual({
      minimum: 500,
      subtotal: 350,
      shortfall: 150,
      message: 'Order total is below the minimum of 500. Add 150 more to place this order.',
    });
  });

  test('parses an ApiError carrying the same field map', () => {
    const err = new ApiError('below minimum', {
      status: false,
      errorCode: 422,
      fieldErrors: {
        code: ['min_order_value_not_met'],
        minimum: ['500'],
        subtotal: ['350'],
      },
    });
    const parsed = parseMinOrderError(err);
    // shortfall is derived when the server omits it.
    expect(parsed).toMatchObject({ minimum: 500, subtotal: 350, shortfall: 150 });
  });

  test('returns null for a different error code', () => {
    const err = {
      response: { data: { message: { errorMessage: { code: ['something_else'] } } } },
    };
    expect(parseMinOrderError(err)).toBeNull();
  });

  test('returns null for a plain error', () => {
    expect(parseMinOrderError(new Error('network'))).toBeNull();
    expect(parseMinOrderError(undefined)).toBeNull();
  });
});
