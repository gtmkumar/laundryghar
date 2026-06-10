/**
 * Tests for rider-mobile/src/api/client.ts — unwrap helpers and ApiError
 *
 * Identical in scope to customer-mobile apiClient tests; kept separate
 * because the rider client is a distinct module that could diverge.
 *
 * Coverage:
 *   - ApiError: name, message, status, errorCode, fieldErrors, instanceof Error
 *   - unwrapSingle: success, status=false, data=null, error propagation
 *   - unwrapList: success, empty data, status=false
 *   - unwrapPaginated: success, pagination flags, status=false, data=null
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

describe('ApiError (rider)', () => {
  test('name is "ApiError"', () => {
    expect(new ApiError('x').name).toBe('ApiError');
  });

  test('message is set', () => {
    expect(new ApiError('rider error').message).toBe('rider error');
  });

  test('status defaults to false', () => {
    expect(new ApiError('x').status).toBe(false);
  });

  test('errorCode propagated', () => {
    expect(new ApiError('x', { errorCode: 403 }).errorCode).toBe(403);
  });

  test('fieldErrors propagated', () => {
    const f = { taskId: ['required'] };
    expect(new ApiError('x', { fieldErrors: f }).fieldErrors).toEqual(f);
  });

  test('is instanceof Error', () => {
    expect(new ApiError('x')).toBeInstanceOf(Error);
  });
});

// ---------------------------------------------------------------------------
// unwrapSingle
// ---------------------------------------------------------------------------

describe('unwrapSingle (rider)', () => {
  test('returns data when status=true', () => {
    const res: SingleResponse<{ riderId: string }> = { status: true, data: { riderId: 'r-1' } };
    expect(unwrapSingle(res)).toEqual({ riderId: 'r-1' });
  });

  test('throws when status=false', () => {
    const res: SingleResponse<unknown> = { status: false, message: { responseMessage: 'Not found' } };
    expect(() => unwrapSingle(res)).toThrow(ApiError);
  });

  test('throws when data is null', () => {
    const res: SingleResponse<unknown> = { status: true, data: null as unknown as undefined };
    expect(() => unwrapSingle(res)).toThrow(ApiError);
  });

  test('fallback message when responseMessage absent', () => {
    try {
      unwrapSingle({ status: false });
    } catch (e) {
      expect((e as ApiError).message).toBe('API error');
    }
  });
});

// ---------------------------------------------------------------------------
// unwrapList
// ---------------------------------------------------------------------------

describe('unwrapList (rider)', () => {
  test('returns data array on success', () => {
    const res: ListResponse<string> = { status: true, data: ['task-1', 'task-2'] };
    expect(unwrapList(res)).toEqual(['task-1', 'task-2']);
  });

  test('returns [] when data undefined but status=true', () => {
    const res: ListResponse<string> = { status: true };
    expect(unwrapList(res)).toEqual([]);
  });

  test('throws when status=false', () => {
    const res: ListResponse<unknown> = { status: false, message: { responseMessage: 'Auth required' } };
    expect(() => unwrapList(res)).toThrow(ApiError);
  });
});

// ---------------------------------------------------------------------------
// unwrapPaginated
// ---------------------------------------------------------------------------

describe('unwrapPaginated (rider)', () => {
  test('returns list and pagination flags', () => {
    const res: PaginatedListResponse<number> = {
      status: true,
      data: { list: [1, 2, 3], hasPreviousPage: true, hasNextPage: false },
    };
    const result = unwrapPaginated(res);
    expect(result.list).toEqual([1, 2, 3]);
    expect(result.hasPreviousPage).toBe(true);
    expect(result.hasNextPage).toBe(false);
  });

  test('returns [] when data.list undefined', () => {
    const res: PaginatedListResponse<number> = {
      status: true,
      data: { list: undefined as unknown as number[], hasPreviousPage: false, hasNextPage: false },
    };
    expect(unwrapPaginated(res).list).toEqual([]);
  });

  test('throws when status=false', () => {
    const res: PaginatedListResponse<unknown> = { status: false };
    expect(() => unwrapPaginated(res)).toThrow(ApiError);
  });

  test('throws when status=true but data=null', () => {
    const res: PaginatedListResponse<unknown> = { status: true, data: null as unknown as undefined };
    expect(() => unwrapPaginated(res)).toThrow(ApiError);
  });
});
