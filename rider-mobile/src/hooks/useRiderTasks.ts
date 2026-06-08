/**
 * useRiderTasks — today's pickup/delivery jobs, merged with session-local
 * completion overrides, plus derived stats for the home + tasks screens.
 */
import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { fetchRiderTasks } from '@/api/tasks';
import { useAuthStore } from '@/store/authStore';
import { useTaskOverrideStore } from '@/store/taskOverrideStore';
import type { RiderTask } from '@/types/api';

export const taskKeys = {
  today: () => ['rider', 'tasks', 'today'] as const,
};

export interface RiderTaskStats {
  total:        number;   // all tasks today
  completed:    number;   // completed today
  pendingCount: number;
  earnedToday:  number;   // ₹ from completed tasks
  avgPayout:    number;   // ₹ average payout across today's tasks
  zoneLabel:    string;   // dominant zone, e.g. "Sec 45"
}

export function useRiderTasks() {
  const accessToken = useAuthStore((s) => s.accessToken);
  const overrides   = useTaskOverrideStore((s) => s.overrides);

  const query = useQuery({
    queryKey: taskKeys.today(),
    queryFn:  fetchRiderTasks,
    enabled:  !!accessToken,
    staleTime: 15_000,
    // Poll while the screen is mounted so newly dispatched tasks auto-populate.
    refetchInterval: 30_000,
    refetchOnMount: 'always',
  });

  const merged = useMemo<RiderTask[]>(() => {
    const base = query.data?.tasks ?? [];
    return base.map((t) => {
      const o = overrides[t.id];
      if (!o) return t;
      return { ...t, status: o.status, completedAt: o.completedAt, rating: o.rating ?? t.rating };
    });
  }, [query.data, overrides]);

  const pending = useMemo(
    () => merged.filter((t) => t.status !== 'completed' && t.status !== 'cancelled' && t.status !== 'failed'),
    [merged],
  );
  const done = useMemo(() => merged.filter((t) => t.status === 'completed'), [merged]);

  const stats = useMemo<RiderTaskStats>(() => {
    const total     = merged.length;
    const completed = done.length;
    const earnedToday = done.reduce((sum, t) => sum + t.payout, 0);
    const avgPayout = total > 0
      ? Math.round(merged.reduce((sum, t) => sum + t.payout, 0) / total)
      : 0;

    // dominant zone among pending tasks
    const zoneCounts = new Map<string, number>();
    for (const t of pending) {
      const z = (t.zoneLabel ?? '').split('·')[0].trim();
      if (z) zoneCounts.set(z, (zoneCounts.get(z) ?? 0) + 1);
    }
    let zoneLabel = 'your zone';
    let best = 0;
    for (const [z, c] of zoneCounts) {
      if (c > best) { best = c; zoneLabel = z; }
    }

    return {
      total,
      completed,
      pendingCount: pending.length,
      earnedToday,
      avgPayout,
      zoneLabel,
    };
  }, [merged, done, pending]);

  return {
    tasks:       merged,
    pending,
    done,
    stats,
    isDemo:      query.data?.isDemo ?? false,
    isLoading:   query.isLoading,
    isError:     query.isError,
    refetch:     query.refetch,
    isRefetching: query.isRefetching,
  };
}

/** Look up a single task by id from the merged set. */
export function useRiderTask(id: string | undefined) {
  const { tasks, isLoading } = useRiderTasks();
  const task = useMemo(() => tasks.find((t) => t.id === id), [tasks, id]);
  return { task, isLoading };
}
