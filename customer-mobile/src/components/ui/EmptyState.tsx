import React from 'react';
import { Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';

interface EmptyStateProps {
  title?: string;
  message?: string;
  icon?: React.ComponentProps<typeof Ionicons>['name'];
}

export function EmptyState({
  title = 'Nothing here yet',
  message,
  icon = 'sparkles-outline',
}: EmptyStateProps) {
  return (
    <View className="flex-1 items-center justify-center gap-2 p-8">
      <View className="mb-2 h-16 w-16 items-center justify-center rounded-3xl bg-cream-200">
        <Ionicons name={icon} size={28} color="#7B7A6C" />
      </View>
      <Text className="text-xl font-bold text-ink">{title}</Text>
      {message ? (
        <Text className="text-center text-base text-ink-muted">{message}</Text>
      ) : null}
    </View>
  );
}
