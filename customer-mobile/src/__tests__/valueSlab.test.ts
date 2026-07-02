/**
 * Tests for src/lib/valueSlab.ts (GitHub #22)
 *
 * Coverage:
 *   - parseValueSlabError: declared_value_required + no_value_slab_match from the raw
 *     Axios 422 envelope, from an ApiError, field extraction (itemId/itemName/declaredValue),
 *     and null for non-matching / plain errors.
 *
 * Pure logic — no native module deps, no mocking beyond a hand-built ApiError.
 */
import { parseValueSlabError } from '../lib/valueSlab';
import { ApiError } from '../api/client';

describe('parseValueSlabError', () => {
  test('parses declared_value_required from a raw Axios 422 envelope', () => {
    const err = {
      response: {
        status: 422,
        data: {
          status: false,
          message: {
            errorTypeCode: 422,
            errorMessage: {
              code: ['declared_value_required'],
              itemId: ['11111111-1111-1111-1111-111111111111'],
              itemName: ['Silk Saree'],
            },
            responseMessage: '“Silk Saree” is priced by declared value — enter the garment\'s value to price this item.',
          },
        },
      },
    };
    expect(parseValueSlabError(err)).toEqual({
      code: 'declared_value_required',
      itemId: '11111111-1111-1111-1111-111111111111',
      itemName: 'Silk Saree',
      declaredValue: undefined,
      message: '“Silk Saree” is priced by declared value — enter the garment\'s value to price this item.',
    });
  });

  test('parses no_value_slab_match with the declared value as a number', () => {
    const err = {
      response: {
        data: {
          message: {
            errorMessage: {
              code: ['no_value_slab_match'],
              itemId: ['22222222-2222-2222-2222-222222222222'],
              declaredValue: ['5000'],
            },
            responseMessage: 'No value slab is configured for a declared value of 5000.',
          },
        },
      },
    };
    expect(parseValueSlabError(err)).toMatchObject({
      code: 'no_value_slab_match',
      itemId: '22222222-2222-2222-2222-222222222222',
      declaredValue: 5000,
    });
  });

  test('parses an ApiError carrying the same field map', () => {
    const err = new ApiError('declared value required', {
      status: false,
      errorCode: 422,
      fieldErrors: {
        code: ['declared_value_required'],
        itemId: ['33333333-3333-3333-3333-333333333333'],
        itemName: ['Leather Jacket'],
      },
    });
    expect(parseValueSlabError(err)).toMatchObject({
      code: 'declared_value_required',
      itemId: '33333333-3333-3333-3333-333333333333',
      itemName: 'Leather Jacket',
    });
  });

  test('returns null for a different structured code (e.g. min-order)', () => {
    const err = {
      response: { data: { message: { errorMessage: { code: ['min_order_value_not_met'] } } } },
    };
    expect(parseValueSlabError(err)).toBeNull();
  });

  test('returns null for a plain error or undefined', () => {
    expect(parseValueSlabError(new Error('network'))).toBeNull();
    expect(parseValueSlabError(undefined)).toBeNull();
  });
});
