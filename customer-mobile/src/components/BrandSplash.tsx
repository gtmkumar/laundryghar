import React from 'react';
import { Text, View } from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { StatusBar } from 'expo-status-bar';
import { MaterialCommunityIcons } from '@expo/vector-icons';

/**
 * Brand splash — shown while the auth store hydrates.
 * Matches the customer splash mockup: olive-green gradient, warm gold glow,
 * centred logo tile, "fresh hai zindagi" tagline.
 */
export function BrandSplash() {
  return (
    <View className="flex-1">
      <StatusBar style="light" />
      <LinearGradient
        colors={['#7C8B49', '#5C6A33', '#3B4423']}
        start={{ x: 0.1, y: 0 }}
        end={{ x: 0.9, y: 1 }}
        style={{ flex: 1 }}
      >
        {/* warm gold glow, top-right */}
        <LinearGradient
          colors={['rgba(219,172,61,0.30)', 'rgba(219,172,61,0)']}
          start={{ x: 1, y: 0 }}
          end={{ x: 0.2, y: 0.6 }}
          style={{ position: 'absolute', top: 0, right: 0, left: 0, bottom: 0 }}
        />

        <View className="flex-1 items-center justify-center px-8">
          <View
            className="mb-7 h-24 w-24 items-center justify-center rounded-[28px] bg-olive-500"
            style={{
              shadowColor: '#000',
              shadowOpacity: 0.25,
              shadowRadius: 16,
              shadowOffset: { width: 0, height: 8 },
            }}
          >
            <MaterialCommunityIcons name="hanger" size={50} color="#FFFFFF" />
          </View>

          <Text className="text-4xl font-extrabold text-white">Laundry Ghar</Text>
          <Text className="mt-3 text-base italic text-white/70">fresh hai zindagi</Text>
        </View>

        <View className="items-center pb-10">
          <Text className="text-xs text-white/55">v2.0 · made in Gurugram</Text>
        </View>
      </LinearGradient>
    </View>
  );
}
