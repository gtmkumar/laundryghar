/**
 * Tests for src/api/client.ts — unwrap helpers and ApiError
 *
 * Coverage:
 *   - unwrapSingle: success path, status=false, data=null, errorCode + fieldErrors propagation
 *   - unwrapList: success, empty data array, status=false
 *   - unwrapPaginated: success, status=false, data=null, pagination flags
 *   - ApiError: name, message, status, errorCode, fieldErrors properties
 *
 * No axios calls are made — only pure envelope-unwrap logic is tested.
 * No native modules required.
 */

import {
  unwrapSingle,
  unwrapList,
  unwrapPaginated,
  ApiError,
} from '../api/client';
import type {
  SingleResponse,
  ListResponse,
  PaginatedListResponse,
} from '../types/api';

// ---------------------------------------------------------------------------
// ApiError
// ---------------------------------------------------------------------------

describe('ApiError', () => {
  test('has name "ApiError"', () => {
    const err = new ApiError('boom');
    expect(err.name).toBe('ApiError');
  });

  test('message is accessible', () => {
    const err = new ApiError('something failed');
    expect(err.message).toBe('something failed');
  });

  test('status defaults to false', () => {
    const err = new ApiError('x');
    expect(err.status).toBe(false);
  });

  test('errorCode propagated', () => {
    const err = new ApiError('x', { errorCode: 1001 });
    expect(err.errorCode).toBe(1001);
  });

  test('fieldErrors propagated', () => {
    const fields = { phone: ['invalid format'] };
    const err = new ApiError('x', { fieldErrors: fields });
    expect(err.fieldErrors).toEqual(fields);
  });

  test('is an instanceof Error', () => {
    expect(new ApiError('x')).toBeInstanceOf(Error);
  });
});

// ---------------------------------------------------------------------------
// unwrapSingle
// ---------------------------------------------------------------------------

describe('unwrapSingle', () => {
  test('returns data when status=true and data present', () => {
    const res: SingleResponse<{ id: string }> = { status: true, data: { id: 'abc' } };
    expect(unwrapSingle(res)).toEqual({ id: 'abc' });
  });

  test('throws ApiError when status=false', () => {
    const res: SingleResponse<unknown> = {
      status: false,
      message: { responseMessage: 'Not found' },
    };
    expect(() => unwrapSingle(res)).toThrow(ApiError);
  });

  test('error message comes from responseMessage', () => {
    const res: SingleResponse<unknown> = {
      status: false,
      message: { responseMessage: 'Access denied' },
    };
    try {
      unwrapSingle(res);
    } catch (e) {
      expect((e as ApiError).message).toBe('Access denied');
    }
  });

  test('throws ApiError when status=true but data is null', () => {
    const res: SingleResponse<unknown> = { status: true, data: null as unknown as undefined };
    expect(() => unwrapSingle(res)).toThrow(ApiError);
  });

  test('errorCode propagated from message', () => {
    const res: SingleResponse<unknown> = {
      status: false,
      message: { errorTypeCode: 422, responseMessage: 'Rule violation' },
    };
    try {
      unwrapSingle(res);
    } catch (e) {
      expect((e as ApiError).errorCode).toBe(422);
    }
  });

  test('fieldErrors propagated from message', () => {
    const res: SingleResponse<unknown> = {
      status: false,
      message: { errorMessage: { email: ['required'] } },
    };
    try {
      unwrapSingle(res);
    } catch (e) {
      expect((e as ApiError).fieldErrors).toEqual({ email: ['required'] });
    }
  });

  test('fallback message when responseMessage absent', () => {
    const res: SingleResponse<unknown> = { status: false };
    try {
      unwrapSingle(res);
    } catch (e) {
      expect((e as ApiError).message).toBe('API error');
    }
  });
});

// ---------------------------------------------------------------------------
// unwrapList
// ---------------------------------------------------------------------------

describe('unwrapList', () => {
  test('returns data array on success', () => {
    const res: ListResponse<number> = { status: true, data: [1, 2, 3] };
    expect(unwrapList(res)).toEqual([1, 2, 3]);
  });

  test('returns empty array when data is undefined but status=true', () => {
    const res: ListResponse<number> = { status: true };
    expect(unwrapList(res)).toEqual([]);
  });

  test('throws ApiError when status=false', () => {
    const res: ListResponse<unknown> = {
      status: false,
      message: { responseMessage: 'Unauthorized' },
    };
    expect(() => unwrapList(res)).toThrow(ApiError);
  });

  test('error message propagated', () => {
    const res: ListResponse<unknown> = {
      status: false,
      message: { responseMessage: 'Brand required' },
    };
    try {
      unwrapList(res);
    } catch (e) {
      expect((e as ApiError).message).toBe('Brand required');
    }
  });
});

// ---------------------------------------------------------------------------
// unwrapPaginated
// ---------------------------------------------------------------------------

describe('unwrapPaginated', () => {
  test('returns list and pagination flags on success', () => {
    const res: PaginatedListResponse<string> = {
      status: true,
      data: {
        list: ['a', 'b'],
        hasPreviousPage: false,
        hasNextPage: true,
      },
    };
    const result = unwrapPaginated(res);
    expect(result.list).toEqual(['a', 'b']);
    expect(result.hasPreviousPage).toBe(false);
    expect(result.hasNextPage).toBe(true);
  });

  test('returns empty list when data.list is undefined', () => {
    const res: PaginatedListResponse<string> = {
      status: true,
      data: {
        list: undefined as unknown as string[],
        hasPreviousPage: false,
        hasNextPage: false,
      },
    };
    expect(unwrapPaginated(res).list).toEqual([]);
  });

  test('throws ApiError when status=false', () => {
    const res: PaginatedListResponse<unknown> = {
      status: false,
      message: { responseMessage: 'Forbidden' },
    };
    expect(() => unwrapPaginated(res)).toThrow(ApiError);
  });

  test('throws ApiError when status=true but data is null', () => {
    const res: PaginatedListResponse<unknown> = {
      status: true,
      data: null as unknown as undefined,
    };
    expect(() => unwrapPaginated(res)).toThrow(ApiError);
  });

  test('pagination: hasNextPage=false signals last page', () => {
    const res: PaginatedListResponse<number> = {
      status: true,
      data: { list: [1], hasPreviousPage: true, hasNextPage: false },
    };
    const result = unwrapPaginated(res);
    expect(result.hasNextPage).toBe(false);
    expect(result.hasPreviousPage).toBe(true);
  });
});
