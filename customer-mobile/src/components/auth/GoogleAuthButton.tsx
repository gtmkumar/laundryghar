/**
 * "Continue with Google" button for the customer app.
 *
 * This exists as its own component for a load-bearing reason: expo-auth-session's
 * `useIdTokenAuthRequest` THROWS ("Client Id property `webClientId` must be defined")
 * when no client ID is configured for the running platform. A hook cannot be called
 * conditionally, so guarding only the button's JSX would still crash the whole screen
 * on an unconfigured build. Isolating the hook in a child that the parent renders only
 * when `isGoogleSignInAvailable()` is true keeps the sign-in screen working with no
 * Google credentials at all — which is the state every environment starts in.
 */
import React, { useCallback, useState } from 'react';
import { ActivityIndicator, Pressable, Text } from 'react-native';
import { FontAwesome } from '@expo/vector-icons';
import { useGoogleSignIn } from '@/lib/googleAuth';

interface Props {
  /** Receives the Google ID token. Should exchange it for a LaundryGhar session. */
  onIdToken: (idToken: string) => void | Promise<void>;
  onError: (message: string) => void;
  /** True while the parent is exchanging the token with the backend. */
  busy?: boolean;
}

export function GoogleAuthButton({ onIdToken, onError, busy = false }: Props) {
  const [prompting, setPrompting] = useState(false);

  const handleIdToken = useCallback(
    (idToken: string) => {
      setPrompting(false);
      void onIdToken(idToken);
    },
    [onIdToken],
  );

  const handleError = useCallback(
    (message: string) => {
      setPrompting(false);
      onError(message);
    },
    [onError],
  );

  const handleDismiss = useCallback(() => setPrompting(false), []);

  const { promptAsync, ready } = useGoogleSignIn({
    onIdToken: handleIdToken,
    onError: handleError,
    onDismiss: handleDismiss,
  });

  const loading = prompting || busy;
  const disabled = !ready || loading;

  return (
    <Pressable
      onPress={() => {
        if (!promptAsync) return;
        // Cleared by onIdToken / onError / onDismiss — every branch the AuthSession
        // response can take resolves the spinner.
        setPrompting(true);
        void promptAsync();
      }}
      disabled={disabled}
      accessibilityRole="button"
      accessibilityLabel="Continue with Google"
      accessibilityState={{ disabled, busy: loading }}
      className={[
        'flex-1 flex-row items-center justify-center gap-2 rounded-2xl border border-cream-300 bg-white py-3.5',
        disabled ? 'opacity-60' : 'active:opacity-80',
      ].join(' ')}
    >
      {loading ? (
        <ActivityIndicator size="small" color="#4A552A" />
      ) : (
        <FontAwesome name="google" size={18} color="#DB4437" />
      )}
      <Text className="text-base font-bold text-ink-soft">Google</Text>
    </Pressable>
  );
}
