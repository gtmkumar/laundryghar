/**
 * Sentry crash-reporting initialisation — customer-mobile.
 *
 * Rules:
 *   - No DSN         → fully disabled (no network, no overhead)
 *   - __DEV__ === true → disabled even when a DSN is set (keep dev consoles clean)
 *   - FEATURES.crashReporting === false → disabled
 *   - Production build with DSN → enabled
 *
 * DSN source (in priority order):
 *   1. Constants.expoConfig.extra.sentryDsn  (set via EAS secret / eas.json env)
 *   2. process.env.EXPO_PUBLIC_SENTRY_DSN    (set via .env.local / CI)
 *
 * Call initialiseSentry() once at the very top of the root layout, before any
 * other provider is mounted, so the first-render crash window is covered.
 *
 * captureError() is a safe wrapper for use in ErrorBoundary and catch blocks —
 * it is a no-op when Sentry is disabled.
 */
import * as Sentry from '@sentry/react-native';
import Constants from 'expo-constants';
import { FEATURES } from '@/constants/config';

// ---------------------------------------------------------------------------
// Resolve DSN at module load time — no side effects yet
// ---------------------------------------------------------------------------

const _extra = (Constants.expoConfig?.extra ?? {}) as Record<string, unknown>;
const _dsn: string =
  (typeof _extra['sentryDsn'] === 'string' ? _extra['sentryDsn'] : undefined) ??
  (typeof process.env['EXPO_PUBLIC_SENTRY_DSN'] === 'string'
    ? process.env['EXPO_PUBLIC_SENTRY_DSN']
    : '') ??
  '';

let _initialised = false;

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Initialise Sentry. Call once at app boot (top of root _layout.tsx).
 * Safe to call multiple times — subsequent calls are no-ops.
 */
export function initialiseSentry(): void {
  if (_initialised) return;
  _initialised = true;

  const enabled = !!(FEATURES.crashReporting && _dsn && !__DEV__);

  Sentry.init({
    dsn: _dsn || undefined,
    enabled,
    // Capture JS errors in the React render tree (works with ErrorBoundary)
    enableNativeNagger: false,
    // Trim stack traces for smaller payloads
    normalizeDepth: 5,
    // Performance tracing — off by default, enable per-session sampling in Sentry UI
    tracesSampleRate: 0,
    // Debug logs in Sentry itself — only if somehow enabled in dev (should never happen)
    debug: false,
  });
}

/**
 * Report an error to Sentry.
 * No-op when Sentry is disabled (no DSN / dev / flag off).
 */
export function captureError(
  error: unknown,
  context?: Record<string, unknown>,
): void {
  if (!_initialised || !_dsn || __DEV__ || !FEATURES.crashReporting) return;

  if (error instanceof Error) {
    Sentry.withScope((scope) => {
      if (context) scope.setExtras(context);
      Sentry.captureException(error);
    });
  } else {
    Sentry.captureMessage(String(error), 'error');
  }
}

/**
 * Wrap the root component with Sentry's error boundary + performance tracing.
 * Must wrap the default export of app/_layout.tsx.
 */
export const withSentry = Sentry.wrap;
