/**
 * ErrorBoundary — rider-mobile.
 *
 * Catches synchronous render-phase errors in the wrapped subtree and shows a
 * branded fallback instead of a white crash screen. Caught errors are reported
 * to Sentry when crash-reporting is enabled.
 *
 * Usage:
 *   Wrap a layout group:
 *     <ErrorBoundary>
 *       <Stack ... />
 *     </ErrorBoundary>
 *
 * The retry button re-mounts the entire subtree by bumping a key.
 */
import React from 'react';
import {
  View,
  Text,
  TouchableOpacity,
  StyleSheet,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { captureError } from '@/lib/sentry';

const CREAM = '#F3EEE3';
const OLIVE = '#4A552A';
const OLIVE_LIGHT = '#E3E7D0';

// ---------------------------------------------------------------------------
// Fallback UI
// ---------------------------------------------------------------------------

function FallbackScreen({ onRetry }: { onRetry: () => void }) {
  return (
    <SafeAreaView style={styles.root}>
      <View style={styles.container}>
        <Text style={styles.title}>Something went wrong</Text>
        <Text style={styles.body}>
          An unexpected error occurred. Please try again.
        </Text>
        <TouchableOpacity
          onPress={onRetry}
          style={styles.button}
          accessibilityRole="button"
          accessibilityLabel="Retry"
        >
          <Text style={styles.buttonText}>Retry</Text>
        </TouchableOpacity>
      </View>
    </SafeAreaView>
  );
}

// ---------------------------------------------------------------------------
// ErrorBoundary class component
// ---------------------------------------------------------------------------

interface Props {
  children: React.ReactNode;
}

interface BoundaryState {
  hasError: boolean;
  error: Error | null;
  /** Bump this key to force a full re-mount of children on retry. */
  retryKey: number;
}

export class ErrorBoundary extends React.Component<Props, BoundaryState> {
  constructor(props: Props) {
    super(props);
    this.state = { hasError: false, error: null, retryKey: 0 };
  }

  static getDerivedStateFromError(error: Error): Partial<BoundaryState> {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, info: React.ErrorInfo): void {
    captureError(error, { componentStack: info.componentStack ?? undefined });
  }

  private handleRetry = () => {
    this.setState((prev) => ({
      hasError: false,
      error: null,
      retryKey: prev.retryKey + 1,
    }));
  };

  render() {
    if (this.state.hasError) {
      return <FallbackScreen onRetry={this.handleRetry} />;
    }

    return (
      <React.Fragment key={this.state.retryKey}>
        {this.props.children}
      </React.Fragment>
    );
  }
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: CREAM,
  },
  container: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: 32,
    gap: 16,
  },
  title: {
    fontSize: 22,
    fontWeight: '700',
    color: OLIVE,
    textAlign: 'center',
  },
  body: {
    fontSize: 15,
    color: '#6B7559',
    textAlign: 'center',
    lineHeight: 22,
  },
  button: {
    marginTop: 8,
    backgroundColor: OLIVE,
    paddingVertical: 14,
    paddingHorizontal: 40,
    borderRadius: 12,
    minWidth: 140,
    alignItems: 'center',
  },
  buttonText: {
    color: OLIVE_LIGHT,
    fontSize: 16,
    fontWeight: '600',
  },
});
