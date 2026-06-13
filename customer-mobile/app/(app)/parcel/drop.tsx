/**
 * Parcel step 2 — "Deliver to".
 * Saved-address picker; the already-chosen pickup address is shown disabled so
 * the drop cannot equal the pickup. Advances to the vehicle screen.
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

export default function ParcelDropScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const { data: addresses, isLoading } = useAddresses();
  const { pickupAddress, dropAddress, setDropAddress, setFareQuote } =
    useBookingStore();

  const [pickerVisible, setPickerVisible] = useState(false);
  const list = addresses ?? [];

  const selected = dropAddress
    ? list.find((a) => a.id === dropAddress.id) ?? null
    : null;

  const handleSelect = (addr: CustomerAddressDto) => {
    if (addr.id === pickupAddress?.id) return; // guard: drop must differ from pickup
    setDropAddress({
      id: addr.id,
      label: addr.label ?? 'Home',
      line1: addr.addressLine1,
    });
    // A new route invalidates any held quote.
    setFareQuote(null);
    setPickerVisible(false);
  };

  // canContinue requires a drop that is not the pickup.
  const canContinue = !!dropAddress && dropAddress.id !== pickupAddress?.id;

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
        <Text className="text-xl font-extrabold text-ink">{t('parcel.deliverTo')}</Text>
      </View>

      <ScrollView
        showsVerticalScrollIndicator={false}
        contentContainerStyle={{ paddingBottom: 130 }}
      >
        <Text className="mx-5 mb-4 text-sm text-ink-muted">{t('parcel.dropHint')}</Text>

        {/* Pickup recap (read-only context) */}
        {pickupAddress ? (
          <View className="mx-5 mb-3 flex-row items-center gap-3 rounded-2xl border border-cream-300 bg-cream-100 p-4">
            <View className="h-9 w-9 items-center justify-center rounded-xl bg-olive-100">
              <Ionicons name="navigate-circle-outline" size={20} color="#5C6A33" />
            </View>
            <View className="flex-1">
              <Text className="text-[11px] font-bold uppercase tracking-wider text-ink-faint">
                {t('parcel.fromLabel')}
              </Text>
              <Text className="text-sm font-bold text-ink" numberOfLines={1}>
                {pickupAddress.line1}
              </Text>
            </View>
          </View>
        ) : null}

        {/* Drop address card */}
        <Pressable
          onPress={() => setPickerVisible(true)}
          className="mx-5 flex-row items-center justify-between rounded-2xl bg-white p-4"
          accessibilityRole="button"
          accessibilityLabel={t('parcel.selectDrop')}
        >
          <View className="flex-1 flex-row items-center gap-3">
            <View className="h-9 w-9 items-center justify-center rounded-xl bg-gold-200">
              <Ionicons name="flag-outline" size={20} color="#8A641D" />
            </View>
            <View className="flex-1">
              <Text className="text-[11px] font-bold uppercase tracking-wider text-ink-faint">
                {t('parcel.deliverTo')}
              </Text>
              <Text className="text-sm font-bold text-ink" numberOfLines={1}>
                {selected
                  ? `${selected.addressLine1}, ${selected.city}`
                  : t('parcel.selectDrop')}
              </Text>
            </View>
          </View>
          <Text className="text-sm font-bold text-olive-700">{t('booking.change')}</Text>
        </Pressable>

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
          onPress={() => router.push('/(app)/parcel/vehicle')}
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
        title={t('parcel.selectDrop')}
        addresses={list}
        selectedId={dropAddress?.id}
        disabledId={pickupAddress?.id}
        disabledNote={t('parcel.samePickupDrop')}
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
