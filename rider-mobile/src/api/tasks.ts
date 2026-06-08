/**
 * Rider tasks API.
 *
 * Target contract (not yet implemented server-side):
 *   GET /api/v1/rider/tasks/today  →  ListResponse<RiderTask>
 *
 * Until the backend ships that route group (FEATURES.riderTasksApi=false),
 * this serves the labelled demo set so the task/delivery/delivered screens
 * work end-to-end. Flip the flag and this transparently goes live.
 */
import { logisticsClient, unwrapList } from '@/api/client';
import { FEATURES } from '@/constants/config';
import { DEMO_TASKS } from '@/data/demoTasks';
import type { ListResponse, RiderTask } from '@/types/api';

export interface RiderTasksResult {
  tasks:  RiderTask[];
  isDemo: boolean;
}

export async function fetchRiderTasks(): Promise<RiderTasksResult> {
  if (FEATURES.riderTasksApi) {
    const res = await logisticsClient.get<ListResponse<RiderTask>>(
      '/rider/tasks/today',
    );
    return { tasks: unwrapList(res.data), isDemo: false };
  }
  return { tasks: DEMO_TASKS, isDemo: true };
}
