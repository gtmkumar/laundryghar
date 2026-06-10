/**
 * Continuous location tracking while the rider is on duty.
 *
 * Prefers a true **background** GPS task (screen off / app backgrounded) so the
 * server-side geofence — auto-flipping a leg to "arrived" at the customer and
 * stamping the drop when a collected pickup reaches the store — and the admin
 * live map keep updating throughout a shift. Background needs a dev/prod build
 * (see backgroundLocation.ts); when it can't start (Expo Go, denied "Always"
 * permission, unsupported), this falls back to a ~25s **foreground** ping while
 * the app is open. Either way an immediate fix is sent on going on-duty and on
 * returning to the foreground. `sendCurrentLocationPing` never throws.
 */
import { useEffect, useRef } from 'react';
import { AppState } from 'react-native';
import { useDutyStore } from '@/store/dutyStore';
import { sendCurrentLocationPing } from '@/lib/sendCurrentLocation';
import { startBackgroundLocation, stopBackgroundLocation } from '@/lib/backgroundLocation';

const PING_INTERVAL_MS = 25_000;

export function useLocationTracking() {
  const isOnDuty = useDutyStore((s) => s.isOnDuty);
  const timer = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    if (!isOnDuty) return;

    let cancelled = false;
    const ping = () => {
      if (!cancelled) void sendCurrentLocationPing(null);
    };

    const startForegroundInterval = () => {
      if (timer.current) return;
      timer.current = setInterval(ping, PING_INTERVAL_MS);
    };

    // Try real background GPS first; only fall back to the foreground interval
    // when it can't run — background updates also fire in the foreground, so we
    // don't want both pinging at once.
    void (async () => {
      const backgroundActive = await startBackgroundLocation();
      if (cancelled) {
        if (backgroundActive) void stopBackgroundLocation();
        return;
      }
      ping(); // immediate fix on going on duty
      if (!backgroundActive) startForegroundInterval();
    })();

    // Returning to the foreground → refresh position right away (a paused
    // foreground interval, or just for snappiness alongside background updates).
    const sub = AppState.addEventListener('change', (s) => {
      if (s === 'active') ping();
    });

    return () => {
      cancelled = true;
      if (timer.current) {
        clearInterval(timer.current);
        timer.current = null;
      }
      sub.remove();
      void stopBackgroundLocation();
    };
  }, [isOnDuty]);
}
