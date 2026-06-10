import React from 'react';
import { Text, View } from 'react-native';

interface OtpInputProps {
  value: string;
  length?: number;
  hasError?: boolean;
  /** Index that should read as "active" (next to fill). */
  activeIndex?: number;
}

/**
 * Display-only OTP cells. Value is driven externally by the custom Keypad.
 * Each cell carries an accessibilityLabel so screen readers can read progress.
 */
export function OtpInput({ value, length = 6, hasError = false, activeIndex }: OtpInputProps) {
  const active = activeIndex ?? value.length;
  const cells = Array.from({ length });

  return (
    <View
      className="flex-row gap-2.5"
      accessible
      accessibilityLabel={`Enter ${length}-digit code. ${value.length} of ${length} digits entered.`}
    >
      {cells.map((_, i) => {
        const char = value[i] ?? '';
        const isActive = i === active;
        const border = hasError
          ? 'border-danger'
          : isActive
            ? 'border-olive-500'
            : char
              ? 'border-olive-200'
              : 'border-cream-300';
        return (
          <View
            key={i}
            className={`h-16 flex-1 items-center justify-center rounded-2xl border-2 bg-white ${border}`}
            accessibilityElementsHidden
            importantForAccessibility="no-hide-descendants"
            style={
              isActive
                ? {
                    shadowColor: '#4A552A',
                    shadowOpacity: 0.12,
                    shadowRadius: 8,
                    shadowOffset: { width: 0, height: 2 },
                    elevation: 2,
                  }
                : undefined
            }
          >
            <Text className="text-2xl font-extrabold text-ink">{char}</Text>
          </View>
        );
      })}
    </View>
  );
}
