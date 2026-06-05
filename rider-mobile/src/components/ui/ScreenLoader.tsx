import React from 'react';
import { ActivityIndicator, View } from 'react-native';

export function ScreenLoader() {
  return (
    <View className="flex-1 items-center justify-center bg-white">
      <ActivityIndicator size="large" color="#15803D" accessibilityLabel="Loading" />
    </View>
  );
}
