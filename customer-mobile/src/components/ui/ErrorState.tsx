import React from 'react';
import { Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
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
      <View className="h-16 w-16 items-center justify-center rounded-3xl bg-red-50">
        <Ionicons name="cloud-offline-outline" size={28} color="#C0492F" />
      </View>
      <Text className="text-center text-base text-ink-muted">{message}</Text>
      {onRetry ? (
        <Button title="Try Again" onPress={onRetry} variant="secondary" iconLeft="refresh" />
      ) : null}
    </View>
  );
}
