/**
 * useOfflineQueueFlush — replays queued status updates when the app is brought
 * to the foreground or when a screen gains focus.
 *
 * Since @react-native-community/netinfo is not installed, we cannot listen for
 * connectivity changes. Instead, we flush on:
 *   1. AppState change to 'active'  (app comes back from background)
 *   2. Any explicit call to flushOfflineQueue() (called from useFocusEffect)
 *
 * Returns { pendingCount, flushOfflineQueue } so screens can show a banner.
 */
import { useEffect, useRef, useCallback } from 'react';
import { AppState, AppStateStatus } from 'react-native';
import { useQueryClient } from '@tanstack/react-query';
import { updateTaskStatus } from '@/api/tasks';
import { useOfflineQueueStore } from '@/store/offlineQueueStore';
import { taskKeys } from '@/hooks/useRiderTasks';

export function useOfflineQueueFlush() {
  const queryClient  = useQueryClient();
  const { queue, isFlushing, setFlushing, dequeue } = useOfflineQueueStore();
  // Stable ref so the AppState listener captures the latest queue without stale closure.
  const queueRef = useRef(queue);
  queueRef.current = queue;

  const flushOfflineQueue = useCallback(async () => {
    if (isFlushing) return;
    const snapshot = [...queueRef.current];
    if (snapshot.length === 0) return;

    setFlushing(true);
    let anySucceeded = false;
    for (const item of snapshot) {
      try {
        await updateTaskStatus(item.taskId, item.status as Parameters<typeof updateTaskStatus>[1]);
        await dequeue(item.taskId, item.status);
        anySucceeded = true;
      } catch {
        // Leave in queue for next flush attempt; stop looping (still offline).
        break;
      }
    }
    setFlushing(false);
    if (anySucceeded) {
      void queryClient.invalidateQueries({ queryKey: taskKeys.today() });
    }
  }, [isFlushing, setFlushing, dequeue, queryClient]);

  // Flush on app foreground.
  useEffect(() => {
    const sub = AppState.addEventListener('change', (state: AppStateStatus) => {
      if (state === 'active') {
        void flushOfflineQueue();
      }
    });
    return () => sub.remove();
  }, [flushOfflineQueue]);

  return {
    pendingCount: queue.length,
    flushOfflineQueue,
  };
}
