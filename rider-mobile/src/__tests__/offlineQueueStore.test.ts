/**
 * Tests for rider-mobile/src/store/offlineQueueStore.ts
 *
 * Coverage:
 *   - hydrate(): loads persisted queue from AsyncStorage, handles empty/null,
 *     handles malformed JSON gracefully
 *   - enqueue(): appends item, sets ts, prevents duplicate (same taskId+status),
 *     persists to AsyncStorage
 *   - dequeue(): removes matched item, leaves others intact, persists
 *   - setFlushing(): toggles isFlushing flag
 *
 * AsyncStorage is mocked via moduleNameMapper (src/__mocks__/async-storage.ts).
 * No native modules required.
 */

import AsyncStorage from '@react-native-async-storage/async-storage';
import { useOfflineQueueStore, OfflineStatusUpdate } from '../store/offlineQueueStore';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function resetStore() {
  useOfflineQueueStore.setState({ queue: [], isFlushing: false });
}

function resetAsyncStorage() {
  // Reset the in-memory store in the mock
  (AsyncStorage as { clear: () => Promise<void> }).clear();
  jest.clearAllMocks();
}

const QUEUE_KEY = 'lg_rider_offline_queue';

function item(overrides: Partial<Omit<OfflineStatusUpdate, 'ts'>> = {}): Omit<OfflineStatusUpdate, 'ts'> {
  return {
    taskId: 'task-1',
    status: 'picked_up',
    ...overrides,
  };
}

// ---------------------------------------------------------------------------
// hydrate
// ---------------------------------------------------------------------------

describe('offlineQueueStore — hydrate()', () => {
  beforeEach(() => {
    resetStore();
    resetAsyncStorage();
  });

  test('starts with empty queue when AsyncStorage is empty', async () => {
    (AsyncStorage.getItem as jest.Mock).mockResolvedValueOnce(null);
    await useOfflineQueueStore.getState().hydrate();
    expect(useOfflineQueueStore.getState().queue).toEqual([]);
  });

  test('loads persisted queue from AsyncStorage', async () => {
    const persisted: OfflineStatusUpdate[] = [
      { taskId: 'task-1', status: 'arrived', ts: 1_000_000 },
    ];
    (AsyncStorage.getItem as jest.Mock).mockResolvedValueOnce(JSON.stringify(persisted));
    await useOfflineQueueStore.getState().hydrate();
    expect(useOfflineQueueStore.getState().queue).toHaveLength(1);
    expect(useOfflineQueueStore.getState().queue[0].taskId).toBe('task-1');
  });

  test('malformed JSON → queue reset to empty, never throws', async () => {
    (AsyncStorage.getItem as jest.Mock).mockResolvedValueOnce('{bad json!!!');
    await expect(useOfflineQueueStore.getState().hydrate()).resolves.not.toThrow();
    expect(useOfflineQueueStore.getState().queue).toEqual([]);
  });

  test('non-array JSON → queue reset to empty', async () => {
    (AsyncStorage.getItem as jest.Mock).mockResolvedValueOnce(JSON.stringify({ not: 'an array' }));
    await useOfflineQueueStore.getState().hydrate();
    expect(useOfflineQueueStore.getState().queue).toEqual([]);
  });
});

// ---------------------------------------------------------------------------
// enqueue
// ---------------------------------------------------------------------------

describe('offlineQueueStore — enqueue()', () => {
  beforeEach(() => {
    resetStore();
    resetAsyncStorage();
  });

  test('appends an item to the queue', async () => {
    await useOfflineQueueStore.getState().enqueue(item());
    expect(useOfflineQueueStore.getState().queue).toHaveLength(1);
  });

  test('sets a numeric ts timestamp', async () => {
    const before = Date.now();
    await useOfflineQueueStore.getState().enqueue(item());
    const after = Date.now();
    const ts = useOfflineQueueStore.getState().queue[0].ts;
    expect(ts).toBeGreaterThanOrEqual(before);
    expect(ts).toBeLessThanOrEqual(after);
  });

  test('persists queue to AsyncStorage', async () => {
    await useOfflineQueueStore.getState().enqueue(item());
    expect(AsyncStorage.setItem).toHaveBeenCalledWith(
      QUEUE_KEY,
      expect.stringContaining('task-1'),
    );
  });

  test('deduplication: same taskId+status is not enqueued twice', async () => {
    await useOfflineQueueStore.getState().enqueue(item({ taskId: 'task-1', status: 'picked_up' }));
    await useOfflineQueueStore.getState().enqueue(item({ taskId: 'task-1', status: 'picked_up' }));
    expect(useOfflineQueueStore.getState().queue).toHaveLength(1);
  });

  test('same taskId but different status is enqueued separately', async () => {
    await useOfflineQueueStore.getState().enqueue(item({ taskId: 'task-1', status: 'picked_up' }));
    await useOfflineQueueStore.getState().enqueue(item({ taskId: 'task-1', status: 'delivered' }));
    expect(useOfflineQueueStore.getState().queue).toHaveLength(2);
  });

  test('different taskIds are enqueued independently', async () => {
    await useOfflineQueueStore.getState().enqueue(item({ taskId: 'task-1' }));
    await useOfflineQueueStore.getState().enqueue(item({ taskId: 'task-2' }));
    expect(useOfflineQueueStore.getState().queue).toHaveLength(2);
  });

  test('optional fields (reason, note) are stored', async () => {
    await useOfflineQueueStore.getState().enqueue({
      taskId: 'task-3',
      status: 'failed',
      reason: 'address not found',
      note: 'customer did not respond',
    });
    const entry = useOfflineQueueStore.getState().queue[0];
    expect(entry.reason).toBe('address not found');
    expect(entry.note).toBe('customer did not respond');
  });
});

// ---------------------------------------------------------------------------
// dequeue
// ---------------------------------------------------------------------------

describe('offlineQueueStore — dequeue()', () => {
  beforeEach(() => {
    resetStore();
    resetAsyncStorage();
  });

  test('removes matched taskId+status', async () => {
    await useOfflineQueueStore.getState().enqueue(item({ taskId: 'task-1', status: 'arrived' }));
    await useOfflineQueueStore.getState().dequeue('task-1', 'arrived');
    expect(useOfflineQueueStore.getState().queue).toHaveLength(0);
  });

  test('leaves other items intact when one is dequeued', async () => {
    await useOfflineQueueStore.getState().enqueue(item({ taskId: 'task-1', status: 'arrived' }));
    await useOfflineQueueStore.getState().enqueue(item({ taskId: 'task-2', status: 'picked_up' }));
    await useOfflineQueueStore.getState().dequeue('task-1', 'arrived');
    const remaining = useOfflineQueueStore.getState().queue;
    expect(remaining).toHaveLength(1);
    expect(remaining[0].taskId).toBe('task-2');
  });

  test('dequeue persists updated queue to AsyncStorage', async () => {
    await useOfflineQueueStore.getState().enqueue(item());
    jest.clearAllMocks();
    await useOfflineQueueStore.getState().dequeue('task-1', 'picked_up');
    expect(AsyncStorage.setItem).toHaveBeenCalledWith(QUEUE_KEY, '[]');
  });

  test('dequeue of non-existent item is a no-op', async () => {
    await useOfflineQueueStore.getState().enqueue(item({ taskId: 'task-1' }));
    await useOfflineQueueStore.getState().dequeue('ghost-task', 'arrived');
    expect(useOfflineQueueStore.getState().queue).toHaveLength(1);
  });
});

// ---------------------------------------------------------------------------
// setFlushing
// ---------------------------------------------------------------------------

describe('offlineQueueStore — setFlushing()', () => {
  beforeEach(resetStore);

  test('sets isFlushing to true', () => {
    useOfflineQueueStore.getState().setFlushing(true);
    expect(useOfflineQueueStore.getState().isFlushing).toBe(true);
  });

  test('sets isFlushing back to false', () => {
    useOfflineQueueStore.getState().setFlushing(true);
    useOfflineQueueStore.getState().setFlushing(false);
    expect(useOfflineQueueStore.getState().isFlushing).toBe(false);
  });

  test('initial isFlushing is false', () => {
    expect(useOfflineQueueStore.getState().isFlushing).toBe(false);
  });
});
