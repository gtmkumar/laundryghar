/**
 * BrandSplash — the in-app branded splash (mockup #1).
 * Shown while the auth store hydrates and momentarily on cold start so the
 * native splash hands off to an identical frame instead of flashing.
 */
import React from 'react';
import { Text, View } from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { StatusBar } from 'expo-status-bar';

export function BrandSplash() {
  return (
    <View
      className="flex-1"
      accessibilityElementsHidden
      importantForAccessibility="no-hide-descendants"
    >
      <StatusBar style="light" />
      <LinearGradient
        colors={['#6E7C42', '#4A552A', '#363F20']}
        start={{ x: 0.1, y: 0 }}
        end={{ x: 0.9, y: 1 }}
        style={{ flex: 1 }}
      >
        {/* warm highlight, top-right (matches the mockup's golden glow) */}
        <LinearGradient
          colors={['rgba(219,172,61,0.35)', 'rgba(219,172,61,0)']}
          start={{ x: 1, y: 0 }}
          end={{ x: 0.2, y: 0.6 }}
          style={{ position: 'absolute', top: 0, right: 0, left: 0, bottom: 0 }}
        />

        <View className="flex-1 items-center justify-center px-8">
          <View
            className="mb-6 h-24 w-24 items-center justify-center rounded-3xl bg-olive-500"
            style={{
              shadowColor: '#000',
              shadowOpacity: 0.25,
              shadowRadius: 16,
              shadowOffset: { width: 0, height: 8 },
            }}
          >
            <MaterialCommunityIcons name="hanger" size={48} color="#FFFFFF" />
          </View>

          <Text className="text-4xl font-extrabold text-white">Laundry Ghar</Text>

          <View className="mt-4 flex-row items-center gap-2 rounded-full border border-white/25 bg-white/10 px-4 py-1.5">
            <MaterialCommunityIcons name="truck-fast-outline" size={16} color="#FFFFFF" />
            <Text className="text-sm font-bold tracking-widest text-white">RIDER</Text>
          </View>
        </View>

        <View className="items-center pb-10">
          <Text className="text-sm text-white/55">Partner app · v2.0</Text>
        </View>
      </LinearGradient>
    </View>
  );
}
