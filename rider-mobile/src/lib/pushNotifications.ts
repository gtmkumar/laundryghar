/**
 * Push notification helper — rider-mobile.
 *
 * Responsibilities:
 *   1. Request OS permission (graceful on denial).
 *   2. Fetch the Expo push token via getExpoPushTokenAsync.
 *   3. Register the token with POST /api/v1/rider/push-token (best-effort).
 *   4. Configure the foreground notification handler.
 *   5. Set up the Android default notification channel.
 *   6. Attach a notification-response listener for deep-linking to task detail.
 *
 * Guarded by FEATURES.pushNotifications — set to false to completely disable.
 *
 * Expo Go / dev-build note
 * ──────────────────────────────────────────────────────────────────────────────
 * getExpoPushTokenAsync() works in Expo Go on Android (limited) but FAILS on
 * iOS Expo Go with "Must be in a native build". This function swallows that
 * error silently and returns null. A development build (eas build --profile
 * development) is required for full iOS functionality.
 * Both platforms require a valid EAS projectId in app.config.ts extra.eas.projectId.
 *
 * Notification data payload contract
 * ──────────────────────────────────────────────────────────────────────────────
 * The Worker's ExpoPushChannelSender currently puts { outboxId, templateCode }
 * in the data field. The deep-link listener below additionally reads:
 *
 *   { type: 'assignment' | 'task', id: string }
 *
 * If the Worker is updated to include these fields, a tap will deep-link to
 * /(app)/tasks/[id]. Until then, taps bring the app to foreground without
 * navigating (graceful no-op). The Worker outbox payload should be extended:
 *   data.type  = "task"
 *   data.id    = <riderAssignmentId>
 *
 * This seam is documented here and NOT addressed in the Worker (out of lane).
 */
import * as Notifications from 'expo-notifications';
import Constants from 'expo-constants';
import { Platform } from 'react-native';
import { router } from 'expo-router';
import { registerPushToken, deactivatePushToken } from '@/api/pushNotifications';
import { FEATURES } from '@/constants/config';

// ---------------------------------------------------------------------------
// Notification data payload shape (as sent by the Worker)
// ---------------------------------------------------------------------------

export interface PushNotificationData {
  /** "task" → navigate to task detail; "assignment" → same. */
  type?: 'task' | 'assignment';
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
let _registeredToken: string | null = null;

/**
 * Initialise push notifications and register the current token with the backend.
 *
 * Safe to call multiple times — re-registers if the token has changed, no-ops
 * otherwise. All errors are caught; the app continues normally on any failure.
 *
 * @returns The Expo push token string, or null if unavailable.
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
      return null;
    }

    // 2. Get the Expo push token
    const projectId =
      (Constants.expoConfig?.extra as { eas?: { projectId?: string } } | undefined)
        ?.eas?.projectId ?? 'laundryghar-rider';

    const { data: expoPushToken } = await Notifications.getExpoPushTokenAsync({ projectId });

    if (!expoPushToken) return null;

    // 3. Register with backend (best-effort)
    const platform = Platform.OS === 'ios' ? 'ios' : 'android';
    try {
      await registerPushToken(expoPushToken, platform);
      _registeredToken = expoPushToken;
    } catch {
      // Network or auth failure — not fatal. Will retry on next launch.
    }

    // 4. Attach response listener — once only
    if (!_cleanupResponseListener) {
      const subscription = Notifications.addNotificationResponseReceivedListener(
        (response) => {
          const data = response.notification.request.content.data as PushNotificationData;
          handleNotificationTap(data);
        },
      );
      _cleanupResponseListener = () => subscription.remove();
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
    // best-effort
  }

  _registeredToken = null;
  _cleanupResponseListener?.();
  _cleanupResponseListener = null;
}

// ---------------------------------------------------------------------------
// Deep-link routing on notification tap
// ---------------------------------------------------------------------------

function handleNotificationTap(data: PushNotificationData): void {
  if (!data?.type || !data?.id) return;

  switch (data.type) {
    case 'task':
    case 'assignment':
      router.push(`/(app)/tasks/${data.id}`);
      break;
    default:
      break;
  }
}
