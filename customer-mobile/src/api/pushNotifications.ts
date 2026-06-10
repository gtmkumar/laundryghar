/**
 * Push token registration API — thin wrappers over the Catalog service endpoints.
 *
 * POST /api/v1/customer/push-token  — register / re-activate an Expo push token
 * DELETE /api/v1/customer/push-token — deactivate on logout
 *
 * Both use the authenticated catalogClient so the JWT is attached automatically.
 */
import { catalogClient } from '@/api/client';

export type PushPlatform = 'ios' | 'android';

/**
 * Register (or re-register) an Expo push token for the signed-in customer.
 * Best-effort — the caller must catch and swallow errors.
 */
export async function registerPushToken(
  token: string,
  platform: PushPlatform,
): Promise<void> {
  await catalogClient.post('/customer/push-token', { token, platform });
}

/**
 * Deactivate an Expo push token on logout.
 * Best-effort — the caller must catch and swallow errors.
 */
export async function deactivatePushToken(token: string): Promise<void> {
  await catalogClient.delete('/customer/push-token', { data: { token } });
}
