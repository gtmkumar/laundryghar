import React from 'react';
import {
  ActivityIndicator,
  Pressable,
  PressableProps,
  Text,
} from 'react-native';

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'warning';
type Size    = 'sm' | 'md' | 'lg';

interface ButtonProps extends Omit<PressableProps, 'style'> {
  title:     string;
  variant?:  Variant;
  size?:     Size;
  loading?:  boolean;
  fullWidth?: boolean;
}

const variantClasses: Record<Variant, { container: string; text: string }> = {
  primary:   { container: 'bg-brand-700',                              text: 'text-white' },
  secondary: { container: 'bg-brand-100 border border-brand-700',      text: 'text-brand-700' },
  ghost:     { container: 'bg-transparent',                            text: 'text-brand-700' },
  danger:    { container: 'bg-red-600',                                text: 'text-white' },
  warning:   { container: 'bg-amber-500',                              text: 'text-white' },
};

const sizeClasses: Record<Size, { container: string; text: string }> = {
  sm: { container: 'px-3 py-2 rounded-lg',  text: 'text-sm'   },
  md: { container: 'px-4 py-3 rounded-xl',  text: 'text-base' },
  lg: { container: 'px-6 py-4 rounded-2xl', text: 'text-lg'   },
};

export function Button({
  title,
  variant  = 'primary',
  size     = 'md',
  loading  = false,
  fullWidth = false,
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
        'flex-row items-center justify-center',
        vc.container,
        sc.container,
        fullWidth ? 'w-full' : 'self-start',
        isDisabled ? 'opacity-50' : 'active:opacity-80',
      ].join(' ')}
    >
      {loading ? (
        <ActivityIndicator
          size="small"
          color={
            variant === 'primary' || variant === 'danger' || variant === 'warning'
              ? '#ffffff'
              : '#15803D'
          }
        />
      ) : (
        <Text className={`font-bold ${vc.text} ${sc.text}`}>{title}</Text>
      )}
    </Pressable>
  );
}
