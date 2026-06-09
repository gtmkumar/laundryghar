import React from 'react';
import { ActivityIndicator, View } from 'react-native';

export function ScreenLoader() {
  return (
    <View className="flex-1 items-center justify-center bg-cream">
      <ActivityIndicator size="large" color="#4A552A" accessibilityLabel="Loading" />
    </View>
  );
}
