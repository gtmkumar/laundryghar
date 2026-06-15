/**
 * Tests for rider-mobile/src/store/taskOverrideStore.ts
 *
 * Coverage:
 *   - complete(): stores an override, returns the override, sets status='completed'
 *   - complete(): completedAt is a valid ISO timestamp close to now
 *   - complete(): rating is NOT fabricated (undefined on fresh override)
 *   - complete(): multiple tasks stored independently
 *   - reset(): clears all overrides
 *   - Idempotency: completing the same task twice overwrites (no duplicate state)
 *
 * No native modules. No UI strings.
 */

import { useTaskOverrideStore } from '../store/taskOverrideStore';

function resetStore() {
  useTaskOverrideStore.getState().reset();
}

// ---------------------------------------------------------------------------
// complete()
// ---------------------------------------------------------------------------

describe('taskOverrideStore — complete()', () => {
  beforeEach(resetStore);

  test('returns an override with status=completed', () => {
    const override = useTaskOverrideStore.getState().complete('task-1');
    expect(override.status).toBe('completed');
  });

  test('completedAt is a valid ISO string', () => {
    const override = useTaskOverrideStore.getState().complete('task-1');
    expect(() => new Date(override.completedAt)).not.toThrow();
    const d = new Date(override.completedAt);
    expect(isNaN(d.getTime())).toBe(false);
  });

  test('completedAt is close to current time (within 2 seconds)', () => {
    const before = Date.now();
    const override = useTaskOverrideStore.getState().complete('task-1');
    const after = Date.now();
    const ts = new Date(override.completedAt).getTime();
    expect(ts).toBeGreaterThanOrEqual(before);
    expect(ts).toBeLessThanOrEqual(after);
  });

  test('rating is undefined (not fabricated by the store)', () => {
    const override = useTaskOverrideStore.getState().complete('task-1');
    expect(override.rating).toBeUndefined();
  });

  test('override is stored in overrides map by taskId', () => {
    useTaskOverrideStore.getState().complete('task-42');
    const stored = useTaskOverrideStore.getState().overrides['task-42'];
    expect(stored).toBeDefined();
    expect(stored.status).toBe('completed');
  });

  test('two different tasks are stored independently', () => {
    useTaskOverrideStore.getState().complete('task-A');
    useTaskOverrideStore.getState().complete('task-B');
    const overrides = useTaskOverrideStore.getState().overrides;
    expect(overrides['task-A']).toBeDefined();
    expect(overrides['task-B']).toBeDefined();
  });

  test('completing same task twice overwrites — no duplicate keys', () => {
    useTaskOverrideStore.getState().complete('task-1');
    const override2 = useTaskOverrideStore.getState().complete('task-1');
    const overrides = useTaskOverrideStore.getState().overrides;
    // Only one key for task-1
    expect(Object.keys(overrides).filter((k) => k === 'task-1')).toHaveLength(1);
    // The stored value matches the second call
    expect(overrides['task-1'].completedAt).toBe(override2.completedAt);
  });
});

// ---------------------------------------------------------------------------
// reset()
// ---------------------------------------------------------------------------

describe('taskOverrideStore — reset()', () => {
  beforeEach(resetStore);

  test('clears all overrides', () => {
    useTaskOverrideStore.getState().complete('task-1');
    useTaskOverrideStore.getState().complete('task-2');
    useTaskOverrideStore.getState().reset();
    expect(Object.keys(useTaskOverrideStore.getState().overrides)).toHaveLength(0);
  });

  test('overrides is empty object after reset', () => {
    useTaskOverrideStore.getState().complete('task-1');
    useTaskOverrideStore.getState().reset();
    expect(useTaskOverrideStore.getState().overrides).toEqual({});
  });

  test('reset is idempotent', () => {
    useTaskOverrideStore.getState().reset();
    useTaskOverrideStore.getState().reset();
    expect(useTaskOverrideStore.getState().overrides).toEqual({});
  });

  test('tasks can be added again after reset', () => {
    useTaskOverrideStore.getState().complete('task-1');
    useTaskOverrideStore.getState().reset();
    useTaskOverrideStore.getState().complete('task-1');
    expect(useTaskOverrideStore.getState().overrides['task-1']).toBeDefined();
  });
});
