import React from 'react';
import { Pressable, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';

interface EmptyStateProps {
  title?: string;
  message?: string;
  icon?: React.ComponentProps<typeof Ionicons>['name'];
  /** Optional CTA button rendered below the message. */
  action?: { label: string; onPress: () => void };
}

export function EmptyState({
  title = 'Nothing here yet',
  message,
  icon = 'sparkles-outline',
  action,
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
      {action ? (
        <Pressable
          onPress={action.onPress}
          accessibilityRole="button"
          accessibilityLabel={action.label}
          className="mt-4 rounded-2xl bg-olive-700 px-6 py-3"
        >
          <Text className="text-sm font-extrabold text-white">{action.label}</Text>
        </Pressable>
      ) : null}
    </View>
  );
}
