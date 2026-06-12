/**
 * useOfflineQueueFlush — replays queued status updates and photo uploads when
 * connectivity is restored or the app returns to the foreground.
 *
 * Since @react-native-community/netinfo is not installed, we cannot listen for
 * connectivity changes. Instead, we flush on:
 *   1. AppState change to 'active'  (app comes back from background)
 *   2. Explicit call to flushOfflineQueue() from the root authenticated layout
 *      (on auth hydration and on the offline→online transition detected by
 *      useNetworkStatus) and from task-detail screen useFocusEffect.
 *
 * Queue item types:
 *   - Normal status updates: { taskId, status: RiderTaskStatus | 'collected' }
 *   - Photo uploads (proof):  { taskId, status: '__photo__', note: '<uri>|<mime>' }
 *
 * Returns { pendingCount, flushOfflineQueue } so screens can show a banner.
 */
import { useEffect, useRef, useCallback } from 'react';
import { AppState, AppStateStatus } from 'react-native';
import { useQueryClient } from '@tanstack/react-query';
import { updateTaskStatus, uploadProofPhoto } from '@/api/tasks';
import { useOfflineQueueStore } from '@/store/offlineQueueStore';
import { taskKeys } from '@/hooks/useRiderTasks';

/** Sentinel status value used to enqueue a proof-photo upload retry. */
export const OFFLINE_PHOTO_STATUS = '__photo__';

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
        if (item.status === OFFLINE_PHOTO_STATUS) {
          // Photo upload retry — note encodes "<uri>|<mime>"
          const [uri, mime] = (item.note ?? '').split('|');
          if (uri && mime) {
            await uploadProofPhoto(item.taskId, uri, mime);
          }
        } else {
          await updateTaskStatus(item.taskId, item.status as Parameters<typeof updateTaskStatus>[1]);
        }
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

  // Flush on app foreground (legacy — also covered by the root layout now).
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
