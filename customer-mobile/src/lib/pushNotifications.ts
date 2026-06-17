/**
 * Push notification helper — customer-mobile.
 *
 * Responsibilities:
 *   1. Request OS permission (graceful on denial).
 *   2. Fetch the Expo push token via getExpoPushTokenAsync.
 *   3. Register the token with POST /api/v1/customer/push-token (best-effort).
 *   4. Configure the foreground notification handler.
 *   5. Set up the Android default notification channel.
 *   6. Attach a notification-response listener for deep-linking.
 *
 * Guarded by FEATURES.pushNotifications — set to false to completely disable.
 *
 * Expo Go / dev-build note
 * ──────────────────────────────────────────────────────────────────────────────
 * getExpoPushTokenAsync() works in Expo Go on Android (limited — no token
 * personalisation) but FAILS on iOS Expo Go with "Must be in a native build".
 * This function swallows that error silently and returns null. A development
 * build (eas build --profile development) is required for full iOS functionality.
 * Both platforms require a valid EAS projectId in app.config.ts extra.eas.projectId.
 *
 * Notification data payload contract
 * ──────────────────────────────────────────────────────────────────────────────
 * The Worker's ExpoPushChannelSender currently puts { outboxId, templateCode }
 * in the data field. The deep-link listener below additionally reads:
 *
 *   { type: 'order' | 'pickup', id: string }
 *
 * If the Worker is updated to include these fields the tap handler will navigate
 * automatically. Until then taps open the app without navigating (graceful no-op).
 * The Worker outbox payload should be extended with:
 *   data.type  = "order"  | "pickup"
 *   data.id    = <orderId> | <pickupRequestId>
 *
 * This seam is documented here and NOT addressed in the Worker (out of lane).
 */
import * as Notifications from 'expo-notifications';
import Constants from 'expo-constants';
import { Platform } from 'react-native';
import { router } from 'expo-router';
import { QueryClient } from '@tanstack/react-query';
import { registerPushToken, deactivatePushToken } from '@/api/pushNotifications';
import { FEATURES } from '@/constants/config';

// ---------------------------------------------------------------------------
// Notification data payload shape (as sent by the Worker)
// ---------------------------------------------------------------------------

export interface PushNotificationData {
  /** "order" → navigate to order detail; "pickup" → navigate to tracking. */
  type?: 'order' | 'pickup';
  /** The entity id to navigate to. */
  id?: string;
  /** Present on all notifications sent by ExpoPushChannelSender. */
  outboxId?: string;
  templateCode?: string;
}

// ---------------------------------------------------------------------------
// Foreground handler — always show alert, play sound
// ---------------------------------------------------------------------------

Notifications.setNotificationHandler({
  handleNotification: async () => ({
    // SDK 54 (expo-notifications): shouldShowAlert split into shouldShowBanner + shouldShowList.
    shouldShowBanner: true,
    shouldShowList:   true,
    shouldPlaySound:  true,
    shouldSetBadge:   false,
  }),
});

// ---------------------------------------------------------------------------
// Android default channel (required for Android 8+)
// ---------------------------------------------------------------------------

async function ensureAndroidChannel(): Promise<void> {
  if (Platform.OS !== 'android') return;
  await Notifications.setNotificationChannelAsync('default', {
    name:             'Default',
    importance:       Notifications.AndroidImportance.HIGH,
    vibrationPattern: [0, 250, 250, 250],
    lightColor:       '#4A552A', // olive brand
  });
}

// ---------------------------------------------------------------------------
// Main initialisation — call after login or on app start when already authed
// ---------------------------------------------------------------------------

let _cleanupResponseListener: (() => void) | null = null;
let _cleanupForegroundListener: (() => void) | null = null;
let _registeredToken: string | null = null;
let _queryClient: QueryClient | null = null;

/** Inject the QueryClient so push handlers can invalidate caches. */
export function setPushQueryClient(qc: QueryClient): void {
  _queryClient = qc;
}

/**
 * Initialise push notifications and register the current token with the backend.
 *
 * Safe to call multiple times — re-registers if the token has changed, no-ops
 * otherwise. All errors are caught; the app continues normally on any failure.
 *
 * @returns The Expo push token string, or null if unavailable (Expo Go iOS, denied).
 */
export async function initialisePushNotifications(): Promise<string | null> {
  if (!FEATURES.pushNotifications) return null;

  try {
    await ensureAndroidChannel();

    // 1. Request permission — graceful on denial
    const { status: existing } = await Notifications.getPermissionsAsync();
    let finalStatus = existing;

    if (existing !== 'granted') {
      const { status } = await Notifications.requestPermissionsAsync();
      finalStatus = status;
    }

    if (finalStatus !== 'granted') {
      // User denied push — that is fine. The app still works; just no push.
      return null;
    }

    // 2. Get the Expo push token — may throw on iOS Expo Go
    const projectId =
      (Constants.expoConfig?.extra as { eas?: { projectId?: string } } | undefined)
        ?.eas?.projectId ?? 'laundryghar-customer';

    const { data: expoPushToken } = await Notifications.getExpoPushTokenAsync({ projectId });

    if (!expoPushToken) return null;

    // 3. Register with backend (best-effort — never block the app)
    const platform = Platform.OS === 'ios' ? 'ios' : 'android';
    try {
      await registerPushToken(expoPushToken, platform);
      _registeredToken = expoPushToken;
    } catch {
      // Network or auth failure — not fatal. Will retry on next launch.
    }

    // 4. Attach response listener (tap-to-deep-link) — once only
    if (!_cleanupResponseListener) {
      const subscription = Notifications.addNotificationResponseReceivedListener(
        (response) => {
          const data = response.notification.request.content.data as PushNotificationData;
          handleNotificationTap(data);
        },
      );
      _cleanupResponseListener = () => subscription.remove();
    }

    // 5. Foreground handler — invalidate relevant query caches (MOB-6)
    if (!_cleanupForegroundListener) {
      const fgSub = Notifications.addNotificationReceivedListener((notification) => {
        const data = notification.request.content.data as PushNotificationData;
        handleForegroundNotification(data);
      });
      _cleanupForegroundListener = () => fgSub.remove();
    }

    return expoPushToken;
  } catch {
    // Expo Go iOS throws here — degrade silently.
    return null;
  }
}

/**
 * Deactivate the registered push token on logout.
 * Must be called before clearing auth state.
 */
export async function deregisterPushNotifications(): Promise<void> {
  if (!FEATURES.pushNotifications || !_registeredToken) return;

  try {
    await deactivatePushToken(_registeredToken);
  } catch {
    // best-effort — token will remain in DB but IsActive = true; harmless after logout
  }

  _registeredToken = null;
  _cleanupResponseListener?.();
  _cleanupResponseListener = null;
  _cleanupForegroundListener?.();
  _cleanupForegroundListener = null;
}

// ---------------------------------------------------------------------------
// Foreground notification handler — invalidate query caches (MOB-6)
// ---------------------------------------------------------------------------

function handleForegroundNotification(data: PushNotificationData): void {
  if (!_queryClient || !data?.id) return;

  if (data.type === 'order') {
    void _queryClient.invalidateQueries({ queryKey: ['orders', 'detail', data.id] });
    void _queryClient.invalidateQueries({ queryKey: ['orders', 'tracking', data.id] });
    void _queryClient.invalidateQueries({ queryKey: ['orders', 'list'] });
  } else if (data.type === 'pickup') {
    void _queryClient.invalidateQueries({ queryKey: ['pickups', 'detail', data.id] });
    void _queryClient.invalidateQueries({ queryKey: ['pickups', 'list'] });
  }
}

// ---------------------------------------------------------------------------
// Deep-link routing on notification tap
// ---------------------------------------------------------------------------

function handleNotificationTap(data: PushNotificationData): void {
  // Invalidate on tap too, so the screen is fresh when it mounts
  handleForegroundNotification(data);

  if (!data?.type || !data?.id) {
    // No specific entity — go to my-orders as the closest useful destination
    router.push('/(app)/(tabs)/my-orders');
    return;
  }

  switch (data.type) {
    case 'order':
      router.push(`/(app)/orders/tracking/${data.id}` as never);
      break;
    case 'pickup':
      router.push(`/(app)/orders/tracking/${data.id}?kind=pickup` as never);
      break;
    default:
      router.push('/(app)/(tabs)/my-orders');
      break;
  }
}
