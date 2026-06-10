/**
 * Push token registration API — thin wrappers over the Logistics service endpoints.
 *
 * POST /api/v1/rider/push-token  — register / re-activate an Expo push token
 * DELETE /api/v1/rider/push-token — deactivate on logout
 *
 * Both use the authenticated logisticsClient so the JWT is attached automatically.
 */
import { logisticsClient } from '@/api/client';

export type PushPlatform = 'ios' | 'android';

/**
 * Register (or re-register) an Expo push token for the signed-in rider.
 * Best-effort — the caller must catch and swallow errors.
 */
export async function registerPushToken(
  token: string,
  platform: PushPlatform,
): Promise<void> {
  await logisticsClient.post('/rider/push-token', { token, platform });
}

/**
 * Deactivate an Expo push token on logout.
 * Best-effort — the caller must catch and swallow errors.
 */
export async function deactivatePushToken(token: string): Promise<void> {
  await logisticsClient.delete('/rider/push-token', { data: { token } });
}
