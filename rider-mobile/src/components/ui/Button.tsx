import React from 'react';
import {
  ActivityIndicator,
  Pressable,
  PressableProps,
  Text,
} from 'react-native';
import { Ionicons } from '@expo/vector-icons';

type Variant = 'primary' | 'confirm' | 'olive' | 'secondary' | 'ghost' | 'danger';
type Size    = 'sm' | 'md' | 'lg';

interface ButtonProps extends Omit<PressableProps, 'style'> {
  title:      string;
  variant?:   Variant;
  size?:      Size;
  loading?:   boolean;
  fullWidth?: boolean;
  /** Optional trailing icon (Ionicons name), e.g. "arrow-forward" */
  iconRight?: React.ComponentProps<typeof Ionicons>['name'];
  iconLeft?:  React.ComponentProps<typeof Ionicons>['name'];
}

const variantClasses: Record<
  Variant,
  { container: string; text: string; spinner: string }
> = {
  // Gold — primary CTA (Send OTP, Start, View tasks, Back to tasks)
  primary:   { container: 'bg-gold-400',                       text: 'text-ink',       spinner: '#1E2119' },
  // Olive — destructive-of-success confirm (Confirm delivered)
  confirm:   { container: 'bg-olive-600',                      text: 'text-white',     spinner: '#ffffff' },
  olive:     { container: 'bg-olive-700',                      text: 'text-white',     spinner: '#ffffff' },
  secondary: { container: 'bg-transparent border border-olive-300', text: 'text-olive-800', spinner: '#4A552A' },
  ghost:     { container: 'bg-transparent',                    text: 'text-olive-700', spinner: '#4A552A' },
  danger:    { container: 'bg-danger',                         text: 'text-white',     spinner: '#ffffff' },
};

const sizeClasses: Record<Size, { container: string; text: string; icon: number }> = {
  sm: { container: 'px-4 py-2.5 rounded-xl',  text: 'text-sm',   icon: 16 },
  md: { container: 'px-5 py-3.5 rounded-2xl', text: 'text-base', icon: 18 },
  lg: { container: 'px-6 py-4 rounded-2xl',   text: 'text-lg',   icon: 20 },
};

export function Button({
  title,
  variant   = 'primary',
  size      = 'md',
  loading   = false,
  fullWidth = false,
  iconRight,
  iconLeft,
  disabled,
  ...rest
}: ButtonProps) {
  const isDisabled = disabled || loading;
  const vc = variantClasses[variant];
  const sc = sizeClasses[size];
  const iconColor = vc.text.includes('white')
    ? '#ffffff'
    : vc.text.includes('olive')
    ? '#4A552A'
    : '#1E2119';

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
            <Ionicons name={iconLeft} size={sc.icon} color={iconColor} />
          ) : null}
          <Text className={`font-bold ${vc.text} ${sc.text}`}>{title}</Text>
          {iconRight ? (
            <Ionicons name={iconRight} size={sc.icon} color={iconColor} />
          ) : null}
        </>
      )}
    </Pressable>
  );
}
