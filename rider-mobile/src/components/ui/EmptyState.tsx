import React from 'react';
import { Text, View } from 'react-native';

interface EmptyStateProps {
  title?:   string;
  message?: string;
}

export function EmptyState({
  title   = 'Nothing here',
  message = 'No items to display.',
}: EmptyStateProps) {
  return (
    <View className="flex-1 items-center justify-center gap-2 p-8">
      <Text className="text-lg font-semibold text-gray-700">{title}</Text>
      <Text className="text-center text-sm text-gray-500">{message}</Text>
    </View>
  );
}
