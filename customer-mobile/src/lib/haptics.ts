/**
 * Haptics helper — thin wrapper around expo-haptics.
 * All calls are fire-and-forget; errors are swallowed so a device
 * without haptics support (simulator, Android without vibrator) never crashes.
 *
 * MOB-9: added for booking confirm success, key taps, and error feedback.
 */
import * as Haptics from 'expo-haptics';

/** Light tap — buttons, selections. */
export function hapticTap(): void {
  void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light).catch(() => undefined);
}

/** Medium impact — important taps (confirm, pay, continue CTAs). */
export function hapticImpact(): void {
  void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium).catch(() => undefined);
}

/** Success notification — booking confirmed, profile saved. */
export function hapticSuccess(): void {
  void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success).catch(() => undefined);
}

/** Error notification — booking failed, validation error. */
export function hapticError(): void {
  void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error).catch(() => undefined);
}

/** Warning notification — insufficient balance, missing field. */
export function hapticWarning(): void {
  void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Warning).catch(() => undefined);
}
