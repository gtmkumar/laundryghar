/**
 * Best-effort one-shot location ping. Requests foreground permission, takes a
 * GPS fix and POSTs it to /rider/location/ping. Never throws — returns a
 * status so callers (e.g. the duty toggle) can surface a hint without blocking.
 */
import * as Location from 'expo-location';
import { postLocationPings } from '@/api/rider';

export type PingOutcome =
  | { ok: true }
  | { ok: false; reason: 'permission' | 'fix' | 'network' };

export async function sendCurrentLocationPing(
  currentAssignmentId?: string | null,
): Promise<PingOutcome> {
  const { status } = await Location.requestForegroundPermissionsAsync();
  if (status !== 'granted') return { ok: false, reason: 'permission' };

  let position: Location.LocationObject;
  try {
    position = await Location.getCurrentPositionAsync({
      accuracy: Location.Accuracy.Balanced,
    });
  } catch {
    return { ok: false, reason: 'fix' };
  }

  try {
    await postLocationPings([
      {
        latitude:       position.coords.latitude,
        longitude:      position.coords.longitude,
        accuracyMeters: position.coords.accuracy ?? null,
        speedKmph:
          position.coords.speed != null ? position.coords.speed * 3.6 : null,
        headingDegrees: position.coords.heading ?? null,
        isMoving:       (position.coords.speed ?? 0) > 0.5,
        currentAssignmentId: currentAssignmentId ?? null,
        pingedAt:       new Date(position.timestamp).toISOString(),
      },
    ]);
    return { ok: true };
  } catch {
    return { ok: false, reason: 'network' };
  }
}
