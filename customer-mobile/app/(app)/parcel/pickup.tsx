/**
 * Parcel step 1 — "Pickup from".
 * Saved-address picker (reuses AddressPickerModal). Seeds from the default
 * address, stores the choice as the parcel pickup, then advances to the drop
 * screen. Mirrors the laundry pickup screen's look.
 */
import React, { useState } from 'react';
import { Pressable, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import { useAddresses } from '@/hooks/useCatalog';
import { useBookingStore } from '@/store/bookingStore';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { AddressPickerModal } from '@/components/ui/AddressPickerModal';
import type { CustomerAddressDto } from '@/types/api';

export default function ParcelPickupScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const { data: addresses, isLoading } = useAddresses();
  const { pickupAddress, setPickupAddress, setJobType, setFareQuote } =
    useBookingStore();

  const [pickerVisible, setPickerVisible] = useState(false);
  const list = addresses ?? [];

  // Mark this flow as a parcel booking and clear any stale quote on entry.
  React.useEffect(() => {
    setJobType('parcel');
    setFareQuote(null);
  }, [setJobType, setFareQuote]);

  const selected = pickupAddress
    ? list.find((a) => a.id === pickupAddress.id) ?? null
    : null;

  const handleSelect = (addr: CustomerAddressDto) => {
    setPickupAddress({
      id: addr.id,
      label: addr.label ?? 'Home',
      line1: addr.addressLine1,
    });
    setPickerVisible(false);
  };

  const canContinue = !!pickupAddress;

  if (isLoading) return <ScreenLoader />;

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
        <Text className="text-xl font-extrabold text-ink">{t('parcel.pickupFrom')}</Text>
      </View>

      <ScrollView
        showsVerticalScrollIndicator={false}
        contentContainerStyle={{ paddingBottom: 130 }}
      >
        <Text className="mx-5 mb-4 text-sm text-ink-muted">{t('parcel.pickupHint')}</Text>

        {/* Chosen / select address card */}
        <Pressable
          onPress={() => setPickerVisible(true)}
          className="mx-5 flex-row items-center justify-between rounded-2xl bg-white p-4"
          accessibilityRole="button"
          accessibilityLabel={t('parcel.selectPickup')}
        >
          <View className="flex-1 flex-row items-center gap-3">
            <View className="h-9 w-9 items-center justify-center rounded-xl bg-olive-100">
              <Ionicons name="navigate-circle-outline" size={20} color="#5C6A33" />
            </View>
            <View className="flex-1">
              <Text className="text-[11px] font-bold uppercase tracking-wider text-ink-faint">
                {t('parcel.pickupFrom')}
              </Text>
              <Text className="text-sm font-bold text-ink" numberOfLines={1}>
                {selected
                  ? `${selected.addressLine1}, ${selected.city}`
                  : t('parcel.selectPickup')}
              </Text>
            </View>
          </View>
          <Text className="text-sm font-bold text-olive-700">{t('booking.change')}</Text>
        </Pressable>

        {/* No address warning */}
        {list.length === 0 ? (
          <Pressable
            onPress={() => router.push('/(app)/addresses' as never)}
            className="mx-5 mt-2 flex-row items-center gap-2 rounded-2xl bg-gold-100 p-3"
          >
            <Ionicons name="information-circle-outline" size={16} color="#8A641D" />
            <Text className="flex-1 text-xs font-semibold text-gold-800">
              {t('parcel.noAddressWarning')}
            </Text>
            <Ionicons name="arrow-forward" size={14} color="#8A641D" />
          </Pressable>
        ) : null}
      </ScrollView>

      {/* Continue */}
      <View
        className="absolute inset-x-0 bottom-0 border-t border-cream-200 bg-white px-6 pb-8 pt-4"
        style={{ borderTopLeftRadius: 28, borderTopRightRadius: 28 }}
      >
        <Pressable
          disabled={!canContinue}
          onPress={() => router.push('/(app)/parcel/drop')}
          className={`flex-row items-center justify-center gap-2 rounded-2xl py-4 ${
            canContinue ? 'bg-gold-400' : 'bg-cream-300'
          }`}
          accessibilityRole="button"
          accessibilityLabel={t('parcel.continue')}
          accessibilityState={{ disabled: !canContinue }}
        >
          <Text
            className={`text-base font-extrabold ${
              canContinue ? 'text-olive-900' : 'text-ink-faint'
            }`}
          >
            {t('parcel.continue')}
          </Text>
          <Ionicons
            name="arrow-forward"
            size={18}
            color={canContinue ? '#2E351C' : '#A8A493'}
          />
        </Pressable>
      </View>

      <AddressPickerModal
        visible={pickerVisible}
        title={t('parcel.selectPickup')}
        addresses={list}
        selectedId={pickupAddress?.id}
        onSelect={handleSelect}
        onClose={() => setPickerVisible(false)}
        onAddNew={() => {
          setPickerVisible(false);
          router.push('/(app)/addresses' as never);
        }}
      />
    </SafeAreaView>
  );
}
