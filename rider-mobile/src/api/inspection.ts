/**
 * Garment inspection API — rider captures condition evidence at pickup.
 *
 *   POST /api/v1/rider/tasks/{id}/inspection
 *     Body: multipart/form-data
 *       front         — front photo (required)
 *       back          — back photo (optional)
 *       conditions    — JSON string of ConditionFlags (stains, tears, buttons)
 *       notes         — free-text rider note (optional, max 500 chars)
 *
 * The endpoint creates an inspection record tied to the delivery_assignment
 * so the warehouse team sees condition photos alongside the order.
 * If the endpoint is not yet live the call will 404/500 — callers should
 * degrade gracefully (the pickup confirm flow is unblocked either way).
 */
import axios from 'axios';
import { ApiError, logisticsClient, unwrapSingle } from '@/api/client';
import type { SingleResponse } from '@/types/api';

export interface ConditionFlags {
  stains:  boolean;
  tears:   boolean;
  missingButtons: boolean;
}

export interface InspectionResult {
  inspectionId: string;
  taskId:       string;
  recordedAt:   string; // ISO-8601
}

function toApiError(e: unknown, fallback: string): ApiError {
  if (axios.isAxiosError(e) && e.response?.data) {
    const env = e.response.data as SingleResponse<unknown>;
    return new ApiError(env.message?.responseMessage ?? fallback, { status: false });
  }
  if (e instanceof ApiError) return e;
  return new ApiError(fallback);
}

export interface InspectionPayload {
  taskId:      string;
  frontUri:    string;
  frontMime:   string;
  backUri?:    string;
  backMime?:   string;
  conditions:  ConditionFlags;
  notes?:      string;
}

/**
 * Submit garment inspection evidence for a pickup task.
 * Front photo is mandatory; back photo is optional.
 * Throws ApiError on network/server failure — callers must handle this and
 * not block the pickup confirm flow on failure (evidence is additive, not gating).
 */
export async function submitInspection(payload: InspectionPayload): Promise<InspectionResult> {
  try {
    const form = new FormData();

    // React Native / Expo FormData file fields expect { uri, name, type }.
    form.append('front', {
      uri:  payload.frontUri,
      name: 'front.jpg',
      type: payload.frontMime,
    } as unknown as Blob);

    if (payload.backUri && payload.backMime) {
      form.append('back', {
        uri:  payload.backUri,
        name: 'back.jpg',
        type: payload.backMime,
      } as unknown as Blob);
    }

    form.append('conditions', JSON.stringify(payload.conditions));

    if (payload.notes?.trim()) {
      form.append('notes', payload.notes.trim());
    }

    const res = await logisticsClient.post<SingleResponse<InspectionResult>>(
      `/rider/tasks/${payload.taskId}/inspection`,
      form,
      {
        headers: { 'Content-Type': 'multipart/form-data' },
        // Photos can be large on slow connections — extend timeout.
        timeout: 90_000,
      },
    );
    return unwrapSingle(res.data);
  } catch (e) {
    throw toApiError(e, 'Could not submit inspection. Try again.');
  }
}
