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
        <Text className="mb-1 text-sm font-medium text-gray-700">{label}</Text>
      ) : null}
      <RNTextInput
        {...rest}
        className={[
          'w-full rounded-xl border bg-white px-4 py-3 text-base text-gray-900',
          error ? 'border-red-500' : 'border-gray-300',
          rest.editable === false ? 'bg-gray-100 text-gray-500' : '',
        ].join(' ')}
        placeholderTextColor="#9CA3AF"
        accessibilityLabel={label}
        accessibilityHint={hint}
      />
      {error ? (
        <Text className="mt-1 text-xs text-red-500" accessibilityRole="alert">
          {error}
        </Text>
      ) : null}
      {hint && !error ? (
        <Text className="mt-1 text-xs text-gray-500">{hint}</Text>
      ) : null}
    </View>
  );
}
