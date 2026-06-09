import React from 'react';
import { View, ViewProps } from 'react-native';

interface CardProps extends ViewProps {
  /** Padding preset. */
  padding?: 'none' | 'sm' | 'md' | 'lg';
  /** Adds a soft elevation shadow. */
  elevated?: boolean;
  className?: string;
}

const PADDING: Record<NonNullable<CardProps['padding']>, string> = {
  none: '',
  sm: 'p-3',
  md: 'p-4',
  lg: 'p-5',
};

/**
 * Rounded white card on the cream canvas. Soft shadow by default.
 */
export function Card({
  padding = 'md',
  elevated = true,
  className = '',
  style,
  children,
  ...rest
}: CardProps) {
  return (
    <View
      {...rest}
      className={['rounded-3xl bg-white', PADDING[padding], className].join(' ')}
      style={[
        elevated
          ? {
              shadowColor: '#2E351C',
              shadowOpacity: 0.06,
              shadowRadius: 12,
              shadowOffset: { width: 0, height: 4 },
              elevation: 2,
            }
          : null,
        style,
      ]}
    >
      {children}
    </View>
  );
}
