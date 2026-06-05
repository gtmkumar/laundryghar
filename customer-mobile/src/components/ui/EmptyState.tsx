import React from 'react';
import { Text, View } from 'react-native';

interface EmptyStateProps {
  title?: string;
  message?: string;
}

export function EmptyState({
  title = 'Nothing here yet',
  message,
}: EmptyStateProps) {
  return (
    <View className="flex-1 items-center justify-center gap-2 p-8">
      <Text className="text-xl font-bold text-gray-700">{title}</Text>
      {message ? (
        <Text className="text-center text-base text-gray-500">{message}</Text>
      ) : null}
    </View>
  );
}
