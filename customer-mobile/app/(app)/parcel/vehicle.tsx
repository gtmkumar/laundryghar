/**
 * Parcel step 3 — "Choose a vehicle".
 * Three selectable tier tiles (Bike / Auto / Car) sent verbatim to the backend
 * as `vehicleTier`. Selecting a tier clears any held quote (price depends on it).
 */
import React from 'react';
import { Pressable, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import { useBookingStore } from '@/store/bookingStore';

type IoniconName = React.ComponentProps<typeof Ionicons>['name'];

interface TierMeta {
  /** Exact backend value sent as vehicleTier. */
  value: string;
  nameKey: string;
  hintKey: string;
  icon: IoniconName;
}

const TIERS: TierMeta[] = [
  { value: 'two_wheeler',   nameKey: 'parcel.tiers.twoWheelerName',   hintKey: 'parcel.tiers.twoWheelerHint',   icon: 'bicycle' },
  { value: 'three_wheeler', nameKey: 'parcel.tiers.threeWheelerName', hintKey: 'parcel.tiers.threeWheelerHint', icon: 'car-sport-outline' },
  { value: 'four_wheeler',  nameKey: 'parcel.tiers.fourWheelerName',  hintKey: 'parcel.tiers.fourWheelerHint',  icon: 'car' },
];

export default function ParcelVehicleScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const { vehicleTier, setVehicleTier, setFareQuote } = useBookingStore();

  const handleSelect = (value: string) => {
    if (value === vehicleTier) return;
    setVehicleTier(value);
    setFareQuote(null); // price depends on tier — invalidate the held quote
  };

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      {/* Header */}
      <View className="flex-row items-center gap-3 px-5 pb-3 pt-2">
        <Pressable
          onPress={() => router.back()}
          className="h-10 w-10 items-center justify-center rounded-full bg-white"
          accessibilityRole="button"
          accessibilityLabel={t('a11y.back')}
        >
          <Ionicons name="chevron-back" size={22} color="#3C3F35" />
        </Pressable>
        <Text className="text-xl font-extrabold text-ink">{t('parcel.chooseVehicle')}</Text>
      </View>

      <ScrollView
        showsVerticalScrollIndicator={false}
        contentContainerStyle={{ paddingBottom: 130 }}
      >
        <Text className="mx-5 mb-4 text-sm text-ink-muted">{t('parcel.vehicleHint')}</Text>

        <View className="mx-5 gap-3" accessibilityRole="radiogroup">
          {TIERS.map((tier) => {
            const selected = tier.value === vehicleTier;
            return (
              <Pressable
                key={tier.value}
                onPress={() => handleSelect(tier.value)}
                accessibilityRole="radio"
                accessibilityState={{ selected }}
                accessibilityLabel={`${t(tier.nameKey)} — ${t(tier.hintKey)}`}
                className={[
                  'flex-row items-center rounded-2xl border bg-white p-4',
                  selected ? 'border-olive-700 bg-olive-50' : 'border-cream-300',
                ].join(' ')}
              >
                <View
                  className={[
                    'mr-3 h-12 w-12 items-center justify-center rounded-2xl',
                    selected ? 'bg-olive-100' : 'bg-cream-100',
                  ].join(' ')}
                >
                  <Ionicons name={tier.icon} size={24} color="#5C6A33" />
                </View>
                <View className="flex-1">
                  <Text className="text-base font-extrabold text-ink">{t(tier.nameKey)}</Text>
                  <Text className="mt-0.5 text-xs text-ink-muted">{t(tier.hintKey)}</Text>
                </View>
                <View
                  className={`h-5 w-5 items-center justify-center rounded-full border-2 ${
                    selected ? 'border-olive-600' : 'border-cream-400'
                  }`}
                >
                  {selected ? <View className="h-2.5 w-2.5 rounded-full bg-olive-600" /> : null}
                </View>
              </Pressable>
            );
          })}
        </View>
      </ScrollView>

      {/* Continue */}
      <View
        className="absolute inset-x-0 bottom-0 border-t border-cream-200 bg-white px-6 pb-8 pt-4"
        style={{ borderTopLeftRadius: 28, borderTopRightRadius: 28 }}
      >
        <Pressable
          onPress={() => router.push('/(app)/parcel/quote')}
          className="flex-row items-center justify-center gap-2 rounded-2xl bg-gold-400 py-4"
          accessibilityRole="button"
          accessibilityLabel={t('parcel.continue')}
        >
          <Text className="text-base font-extrabold text-olive-900">{t('parcel.continue')}</Text>
          <Ionicons name="arrow-forward" size={18} color="#2E351C" />
        </Pressable>
      </View>
    </SafeAreaView>
  );
}
