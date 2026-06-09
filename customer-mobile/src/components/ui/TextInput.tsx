import React from 'react';
import {
  Text,
  TextInput as RNTextInput,
  TextInputProps,
  View,
} from 'react-native';

interface InputProps extends TextInputProps {
  label?: string;
  error?: string;
  hint?: string;
}

export function TextInput({ label, error, hint, ...rest }: InputProps) {
  return (
    <View className="w-full">
      {label ? (
        <Text className="mb-1.5 text-xs font-bold uppercase tracking-wider text-ink-muted">
          {label}
        </Text>
      ) : null}
      <RNTextInput
        {...rest}
        className={[
          'w-full rounded-2xl border bg-white px-4 py-3.5 text-base text-ink',
          error ? 'border-danger' : 'border-cream-300',
          rest.editable === false ? 'bg-cream-100 text-ink-muted' : '',
        ].join(' ')}
        placeholderTextColor="#A8A493"
        accessibilityLabel={label}
        accessibilityHint={hint}
      />
      {error ? (
        <Text className="mt-1.5 text-xs text-danger" accessibilityRole="alert">
          {error}
        </Text>
      ) : null}
      {hint && !error ? (
        <Text className="mt-1.5 text-xs text-ink-faint">{hint}</Text>
      ) : null}
    </View>
  );
}
