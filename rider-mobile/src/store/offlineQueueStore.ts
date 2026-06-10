/**
 * Offline queue — persists task status-update payloads to AsyncStorage when
 * the network is unavailable, then replays them on app foreground / screen focus.
 *
 * @react-native-community/netinfo is NOT installed, so we rely on:
 *   - AppState 'active' event (replay on foreground)
 *   - explicit flushOfflineQueue() calls from each screen's useFocusEffect
 *
 * The PATCH /api/v1/rider/tasks/{id}/status is idempotent for the same status
 * (same status applied twice on the server is a no-op — the handler guards
 * against invalid transitions and re-saves the same state silently).
 */
import AsyncStorage from '@react-native-async-storage/async-storage';
import { create } from 'zustand';

const QUEUE_KEY = 'lg_rider_offline_queue';

export interface OfflineStatusUpdate {
  taskId:   string;
  status:   string;
  reason?:  string;
  note?:    string;
  ts:       number;   // Date.now() when enqueued
}

interface OfflineQueueState {
  queue:       OfflineStatusUpdate[];
  /** Whether a flush is currently running (prevents concurrent flushes). */
  isFlushing:  boolean;

  /** Load persisted queue from AsyncStorage into memory. Call once at app boot. */
  hydrate:     () => Promise<void>;

  /** Append a failed status update to the queue and persist it. */
  enqueue:     (item: Omit<OfflineStatusUpdate, 'ts'>) => Promise<void>;

  /** Remove a successfully-replayed item from the queue. */
  dequeue:     (taskId: string, status: string) => Promise<void>;

  /** Mark flushing started/stopped. */
  setFlushing: (v: boolean) => void;
}

export const useOfflineQueueStore = create<OfflineQueueState>()((set, get) => ({
  queue:      [],
  isFlushing: false,

  hydrate: async () => {
    try {
      const raw = await AsyncStorage.getItem(QUEUE_KEY);
      if (raw) {
        const parsed = JSON.parse(raw) as OfflineStatusUpdate[];
        set({ queue: Array.isArray(parsed) ? parsed : [] });
      }
    } catch {
      // Malformed data — start with empty queue.
      set({ queue: [] });
    }
  },

  enqueue: async (item) => {
    const entry: OfflineStatusUpdate = { ...item, ts: Date.now() };
    // Deduplicate: if the same taskId+status is already queued, skip.
    const current = get().queue;
    if (current.some((q) => q.taskId === item.taskId && q.status === item.status)) return;
    const next = [...current, entry];
    set({ queue: next });
    await AsyncStorage.setItem(QUEUE_KEY, JSON.stringify(next));
  },

  dequeue: async (taskId, status) => {
    const next = get().queue.filter((q) => !(q.taskId === taskId && q.status === status));
    set({ queue: next });
    await AsyncStorage.setItem(QUEUE_KEY, JSON.stringify(next));
  },

  setFlushing: (v) => set({ isFlushing: v }),
}));
