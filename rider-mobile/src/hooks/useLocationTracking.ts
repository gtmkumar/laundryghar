/**
 * Continuous location tracking while the rider is on duty.
 *
 * Sends a GPS ping every ~25s (and immediately on going on-duty / on app resume)
 * to POST /rider/location/ping. This is what feeds the server-side geofence —
 * auto-flipping a leg to "arrived" when the rider reaches the customer, and
 * stamping the drop when a collected pickup reaches the store — and keeps the
 * admin live map fresh.
 *
 * Foreground only: it pings while the app is open/active. True background
 * tracking (screen off, app backgrounded) needs native config — an
 * expo-location background task + UIBackgroundModes/foreground-service — and is
 * a deliberate follow-up. `sendCurrentLocationPing` never throws, so a denied
 * permission or a missing fix just skips that tick.
 */
import { useEffect, useRef } from 'react';
import { AppState } from 'react-native';
import { useDutyStore } from '@/store/dutyStore';
import { sendCurrentLocationPing } from '@/lib/sendCurrentLocation';

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

    ping(); // immediate fix while on duty
    timer.current = setInterval(ping, PING_INTERVAL_MS);

    // Returning to the foreground → refresh position right away (the interval
    // may have been throttled/paused while backgrounded).
    const sub = AppState.addEventListener('change', (s) => {
      if (s === 'active') ping();
    });

    return () => {
      cancelled = true;
      if (timer.current) clearInterval(timer.current);
      timer.current = null;
      sub.remove();
    };
  }, [isOnDuty]);
}
