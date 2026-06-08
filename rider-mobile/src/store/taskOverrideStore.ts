/**
 * Task override store — session-local completion state for rider tasks.
 *
 * Because there is no backend rider-task endpoint yet, when a rider confirms a
 * pickup/delivery in the app we record the completion here so the tasks list,
 * the stat tiles and the "delivered" summary stay consistent for the rest of
 * the session. Not persisted (resets on relaunch) — purely a demo-flow seam.
 * Once the real endpoints exist, the complete action becomes a mutation and
 * this store can be deleted.
 */
import { create } from 'zustand';

export interface TaskOverride {
  status:      'completed';
  completedAt: string;   // ISO
  rating:      number;   // simulated customer rating
}

interface TaskOverrideState {
  overrides: Record<string, TaskOverride>;
  complete:  (taskId: string) => TaskOverride;
  reset:     () => void;
}

export const useTaskOverrideStore = create<TaskOverrideState>()((set, get) => ({
  overrides: {},
  complete: (taskId) => {
    const override: TaskOverride = {
      status:      'completed',
      completedAt: new Date().toISOString(),
      rating:      5,
    };
    set({ overrides: { ...get().overrides, [taskId]: override } });
    return override;
  },
  reset: () => set({ overrides: {} }),
}));
