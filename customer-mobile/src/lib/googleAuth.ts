/**
 * Google sign-in via expo-auth-session.
 *
 * Why AuthSession rather than a native Google SDK: this app ships to Expo web,
 * Android and iOS from one codebase, and AuthSession is pure JS on all three — no
 * native module, so it also works in Expo Go. The backend never sees a Google
 * access token; it receives only the ID token and verifies it against Google's JWKS
 * (see IGoogleIdTokenVerifier), so nothing here is trusted client-side.
 *
 * The flow requests the `id_token` response type directly. We do not need an access
 * token: identity is the whole point, and skipping the code exchange means no client
 * secret has to exist anywhere near the app.
 */
import { useEffect } from 'react';
import { Platform } from 'react-native';
import * as Google from 'expo-auth-session/providers/google';
import * as WebBrowser from 'expo-web-browser';
import { GOOGLE_AUTH, FEATURES } from '@/constants/config';

// Required so the auth popup/redirect can hand control back to the app.
// No-op on native; on web it closes the popup window opened for the Google consent screen.
WebBrowser.maybeCompleteAuthSession();

/**
 * Whether a Google client ID exists for the platform this build is running on.
 *
 * This gates whether GoogleAuthButton is MOUNTED at all, which matters more than it
 * looks: `useIdTokenAuthRequest` throws — taking the sign-in screen down with it — when
 * the ID its platform requires is missing. So this check must be as strict as the
 * provider's own requirement, not merely optimistic.
 *
 * Each platform needs its OWN client ID: the provider demands `webClientId` on web,
 * `androidClientId` on Android (bound to package + SHA-1), `iosClientId` on iOS
 * (bound to the bundle id). One is not a substitute for another.
 */
export function isGoogleSignInAvailable(): boolean {
  if (!FEATURES.socialLogin) return false;

  const required =
    Platform.OS === 'android' ? GOOGLE_AUTH.androidClientId
    : Platform.OS === 'ios'   ? GOOGLE_AUTH.iosClientId
    : GOOGLE_AUTH.webClientId;

  return Boolean(required && required.trim());
}

export interface UseGoogleSignInOptions {
  /** Called with the Google ID token once the user completes the consent screen. */
  onIdToken: (idToken: string) => void;
  /** Called when Google returns an error. Dismissal/cancellation does NOT call this. */
  onError: (message: string) => void;
  /**
   * Called when the user backs out of the consent screen. Not an error — but the caller
   * still has to clear its spinner, which is why this is separate from onError.
   */
  onDismiss?: () => void;
}

export interface UseGoogleSignInResult {
  /** Opens the Google consent screen. Null until the request object is ready. */
  promptAsync: (() => Promise<unknown>) | null;
  /** False while the auth request is still being constructed (PKCE state, discovery). */
  ready: boolean;
}

/**
 * Hook wrapper around the Google provider. Hands the caller a `promptAsync` and
 * reports the resulting ID token through `onIdToken`.
 */
export function useGoogleSignIn({
  onIdToken,
  onError,
  onDismiss,
}: UseGoogleSignInOptions): UseGoogleSignInResult {
  const [request, response, promptAsync] = Google.useIdTokenAuthRequest({
    webClientId: GOOGLE_AUTH.webClientId,
    androidClientId: GOOGLE_AUTH.androidClientId,
    iosClientId: GOOGLE_AUTH.iosClientId,
  });

  useEffect(() => {
    if (!response) return;

    if (response.type === 'success') {
      // The provider surfaces the ID token in params for the implicit id_token flow
      // and on authentication for the code flow; check both so a provider-version
      // change does not silently break sign-in.
      const idToken =
        response.params?.id_token ?? response.authentication?.idToken ?? null;

      if (idToken) {
        onIdToken(idToken);
      } else {
        onError('Google did not return an ID token. Please try again.');
      }
      return;
    }

    if (response.type === 'error') {
      onError(response.error?.message ?? 'Google sign-in failed. Please try again.');
      return;
    }

    // 'dismiss' / 'cancel' / 'locked' are the user backing out — no dialog, but the
    // caller still needs to stop showing a spinner.
    onDismiss?.();
  }, [response, onIdToken, onError, onDismiss]);

  return {
    promptAsync: request ? () => promptAsync() : null,
    ready: Boolean(request),
  };
}
