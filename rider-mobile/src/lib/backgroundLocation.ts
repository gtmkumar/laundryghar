/**
 * True background GPS for active shifts.
 *
 * `useLocationTracking` only pings while the app is foreground. This adds a real
 * background location task (screen off / app backgrounded) via expo-location +
 * expo-task-manager, so the server-side geofence (arrive / drop) and the admin
 * live map keep updating during a shift even when the rider isn't looking at the
 * app.
 *
 * Background location requires a **dev/production build** with the native config
 * in app.config.ts (UIBackgroundModes + ACCESS_BACKGROUND_LOCATION + foreground
 * service) — it is NOT available in Expo Go. Every entry point is best-effort and
 * never throws; callers fall back to the foreground interval when it can't start.
 *
 * The task is registered at module load (TaskManager.defineTask must run before
 * the OS delivers updates), so this module is imported once at app startup.
 */
import * as Location from 'expo-location';
import * as TaskManager from 'expo-task-manager';
import { postLocationPings } from '@/api/rider';

export const BG_LOCATION_TASK = 'laundryghar-rider-background-location';

interface LocationTaskData {
  locations?: Location.LocationObject[];
}

// Registered once, at import time. Receives a batch of buffered locations and
// pings the freshest one (the geofence only needs the latest position).
TaskManager.defineTask(BG_LOCATION_TASK, async ({ data, error }) => {
  if (error) return;
  const locations = (data as LocationTaskData | undefined)?.locations;
  const latest = locations?.[locations.length - 1];
  if (!latest) return;

  try {
    await postLocationPings([
      {
        latitude: latest.coords.latitude,
        longitude: latest.coords.longitude,
        accuracyMeters: latest.coords.accuracy ?? null,
        speedKmph: latest.coords.speed != null ? latest.coords.speed * 3.6 : null,
        headingDegrees: latest.coords.heading ?? null,
        isMoving: (latest.coords.speed ?? 0) > 0.5,
        currentAssignmentId: null,
        pingedAt: new Date(latest.timestamp).toISOString(),
      },
    ]);
  } catch {
    // Offline / transient — the OS will deliver the next batch; nothing to do.
  }
});

/** True once background updates are running for our task. */
export async function isBackgroundLocationRunning(): Promise<boolean> {
  try {
    return await Location.hasStartedLocationUpdatesAsync(BG_LOCATION_TASK);
  } catch {
    return false;
  }
}

/**
 * Start background GPS for an active shift. Returns `true` only when background
 * updates are actually running — `false` on Expo Go, denied "Always" permission,
 * or any platform error, so the caller can fall back to foreground pings.
 */
export async function startBackgroundLocation(): Promise<boolean> {
  try {
    // Foreground must be granted before Always can be requested.
    const fg = await Location.requestForegroundPermissionsAsync();
    if (fg.status !== 'granted') return false;
    const bg = await Location.requestBackgroundPermissionsAsync();
    if (bg.status !== 'granted') return false;

    if (await isBackgroundLocationRunning()) return true;

    await Location.startLocationUpdatesAsync(BG_LOCATION_TASK, {
      accuracy: Location.Accuracy.Balanced,
      timeInterval: 25_000,      // ~ the foreground cadence
      distanceInterval: 30,      // …or every 30 m, whichever comes first
      pausesUpdatesAutomatically: false,
      // Android: a persistent notification is mandatory for background location.
      foregroundService: {
        notificationTitle: 'Laundry Ghar Rider — on duty',
        notificationBody: 'Sharing your location for pickups & deliveries.',
        notificationColor: '#4A552A',
      },
      // iOS: a moving-vehicle context + the blue status bar while tracking.
      activityType: Location.ActivityType.AutomotiveNavigation,
      showsBackgroundLocationIndicator: true,
    });
    return await isBackgroundLocationRunning();
  } catch {
    return false; // Expo Go or unsupported → caller uses the foreground interval.
  }
}

/** Stop background GPS (going off duty / sign-out). Safe to call when not running. */
export async function stopBackgroundLocation(): Promise<void> {
  try {
    if (await isBackgroundLocationRunning()) {
      await Location.stopLocationUpdatesAsync(BG_LOCATION_TASK);
    }
  } catch {
    // Already stopped / unsupported — nothing to do.
  }
}
