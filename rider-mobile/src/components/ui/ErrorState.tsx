import React from 'react';
import { Text, View } from 'react-native';
import { Button } from './Button';

interface ErrorStateProps {
  message?: string;
  onRetry?: () => void;
}

export function ErrorState({
  message = 'Something went wrong. Please try again.',
  onRetry,
}: ErrorStateProps) {
  return (
    <View className="flex-1 items-center justify-center gap-4 p-8">
      <Text className="text-center text-base text-gray-600">{message}</Text>
      {onRetry ? (
        <Button title="Try Again" onPress={onRetry} variant="secondary" />
      ) : null}
    </View>
  );
}
