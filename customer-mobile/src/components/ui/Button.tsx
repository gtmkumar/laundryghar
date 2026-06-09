import React from 'react';
import {
  ActivityIndicator,
  Pressable,
  PressableProps,
  Text,
  View,
} from 'react-native';
import { Ionicons } from '@expo/vector-icons';

type Variant = 'primary' | 'olive' | 'confirm' | 'secondary' | 'ghost' | 'danger';
type Size = 'sm' | 'md' | 'lg';

interface ButtonProps extends Omit<PressableProps, 'style'> {
  title: string;
  variant?: Variant;
  size?: Size;
  loading?: boolean;
  fullWidth?: boolean;
  iconLeft?: React.ComponentProps<typeof Ionicons>['name'];
  iconRight?: React.ComponentProps<typeof Ionicons>['name'];
}

const variantClasses: Record<Variant, { container: string; text: string; spinner: string }> = {
  primary:   { container: 'bg-gold-400',                              text: 'text-olive-900', spinner: '#2E351C' },
  olive:     { container: 'bg-olive-700',                            text: 'text-white',     spinner: '#FFFFFF' },
  confirm:   { container: 'bg-olive-600',                            text: 'text-white',     spinner: '#FFFFFF' },
  secondary: { container: 'bg-transparent border border-olive-300', text: 'text-olive-800', spinner: '#4A552A' },
  ghost:     { container: 'bg-transparent',                          text: 'text-olive-700', spinner: '#4A552A' },
  danger:    { container: 'bg-danger',                               text: 'text-white',     spinner: '#FFFFFF' },
};

const sizeClasses: Record<Size, { container: string; text: string; icon: number }> = {
  sm: { container: 'px-4 py-2.5 rounded-xl',  text: 'text-sm',  icon: 16 },
  md: { container: 'px-5 py-3.5 rounded-2xl', text: 'text-base', icon: 18 },
  lg: { container: 'px-6 py-4 rounded-2xl',   text: 'text-lg',  icon: 20 },
};

const ICON_COLORS: Record<Variant, string> = {
  primary:   '#2E351C',
  olive:     '#FFFFFF',
  confirm:   '#FFFFFF',
  secondary: '#4A552A',
  ghost:     '#4A552A',
  danger:    '#FFFFFF',
};

export function Button({
  title,
  variant = 'primary',
  size = 'md',
  loading = false,
  fullWidth = false,
  iconLeft,
  iconRight,
  disabled,
  ...rest
}: ButtonProps) {
  const isDisabled = disabled || loading;
  const vc = variantClasses[variant];
  const sc = sizeClasses[size];

  return (
    <Pressable
      {...rest}
      disabled={isDisabled}
      accessibilityRole="button"
      accessibilityLabel={title}
      accessibilityState={{ disabled: isDisabled, busy: loading }}
      className={[
        'flex-row items-center justify-center gap-2',
        vc.container,
        sc.container,
        fullWidth ? 'w-full' : 'self-start',
        isDisabled ? 'opacity-40' : 'active:opacity-85',
      ].join(' ')}
    >
      {loading ? (
        <ActivityIndicator size="small" color={vc.spinner} />
      ) : (
        <>
          {iconLeft ? (
            <Ionicons name={iconLeft} size={sc.icon} color={ICON_COLORS[variant]} />
          ) : null}
          <Text className={`font-bold ${vc.text} ${sc.text}`}>{title}</Text>
          {iconRight ? (
            <Ionicons name={iconRight} size={sc.icon} color={ICON_COLORS[variant]} />
          ) : null}
        </>
      )}
    </Pressable>
  );
}

/** Lightweight container alias used by a few screens that want a non-Pressable shell. */
export function ButtonRow({ children }: { children: React.ReactNode }) {
  return <View className="flex-row gap-3">{children}</View>;
}
