/**
 * Booking step 1 — "What needs washing?"
 * Item picker with quantity steppers, built on the live price list
 * (GET {Catalog}/customer/catalog/price-list) with a demo fallback so the
 * flow always works in dev. Selection lives in the cart store.
 */
import React, { useMemo, useState } from 'react';
import { FlatList, Pressable, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { usePriceList } from '@/hooks/useCatalog';
import { useCartStore } from '@/store/cartStore';
import { useBookingStore } from '@/store/bookingStore';
import { Stepper } from '@/components/ui/Stepper';
import { Chip } from '@/components/ui/Chip';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { rupees } from '@/lib/format';
import { DEMO_ITEMS } from '@/data/demoItems';

interface PickerItem {
  id: string;
  name: string;
  fabric: string;
  unitPrice: number;
}

function ItemRow({ item }: { item: PickerItem }) {
  const lines = useCartStore((s) => s.lines);
  const setQty = useCartStore((s) => s.setQty);
  const qty = lines[item.id]?.qty ?? 0;

  return (
    <View className="flex-row items-center border-b border-cream-200 py-3.5">
      <View className="mr-3 h-11 w-11 items-center justify-center rounded-xl bg-cream-100">
        <Ionicons name="shirt-outline" size={20} color="#5C6A33" />
      </View>
      <View className="flex-1">
        <Text className="text-base font-bold text-ink">{item.name}</Text>
        <Text className="text-xs text-ink-muted">
          {item.fabric} · {rupees(item.unitPrice)}/pc
        </Text>
      </View>
      <Stepper
        value={qty}
        onChange={(next) =>
          setQty(
            { id: item.id, name: item.name, service: item.fabric, unitPrice: item.unitPrice },
            next,
          )
        }
      />
    </View>
  );
}

export default function ItemsScreen() {
  const router = useRouter();
  const { data: priceList, isLoading } = usePriceList();
  const count = useCartStore((s) => s.count());
  const subtotal = useCartStore((s) => s.subtotal());
  const express = useBookingStore((s) => s.express);
  const setExpress = useBookingStore((s) => s.setExpress);
  const [serviceFilter, setServiceFilter] = useState<'all' | 'express'>('all');

  const items: PickerItem[] = useMemo(() => {
    const live = (priceList ?? [])
      .filter((p) => p.isActive)
      .map((p) => ({
        id: p.id,
        name: p.displayLabel ?? p.notes ?? 'Garment',
        fabric: p.notes ?? 'Standard',
        unitPrice: p.basePrice,
      }));
    return live.length > 0 ? live : DEMO_ITEMS;
  }, [priceList]);

  // Express surcharge mirrors the mockup (+₹50 flat shown later); here we only
  // tag the booking. Estimate total = subtotal (+ express handled at payment).
  const estimate = subtotal;

  if (isLoading) return <ScreenLoader />;

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      {/* Header */}
      <View className="flex-row items-center gap-3 px-5 pt-2 pb-3">
        <Pressable
          onPress={() => router.back()}
          className="h-10 w-10 items-center justify-center rounded-full bg-white"
          accessibilityLabel="Go back"
        >
          <Ionicons name="chevron-back" size={22} color="#3C3F35" />
        </Pressable>
        <Text className="text-xl font-extrabold text-ink">What needs washing?</Text>
      </View>

      {/* Filter chips */}
      <View className="flex-row px-5 pb-2">
        <Chip label="Dry Clean" selected={serviceFilter === 'all'} onPress={() => setServiceFilter('all')} />
        <Chip
          label="Express"
          icon="flash"
          accent="gold"
          selected={express}
          onPress={() => setExpress(!express)}
        />
      </View>

      {/* Items */}
      <FlatList
        data={items}
        keyExtractor={(i) => i.id}
        renderItem={({ item }) => <ItemRow item={item} />}
        contentContainerStyle={{ paddingHorizontal: 20, paddingBottom: 140 }}
        showsVerticalScrollIndicator={false}
      />

      {/* Total bar */}
      <View
        className="absolute inset-x-0 bottom-0 flex-row items-center justify-between border-t border-cream-200 bg-white px-6 pb-8 pt-4"
        style={{ borderTopLeftRadius: 28, borderTopRightRadius: 28 }}
      >
        <View>
          <Text className="text-xs text-ink-muted">
            {count} item{count !== 1 ? 's' : ''}{express ? ' · Express' : ''}
          </Text>
          <Text className="text-2xl font-extrabold text-ink">{rupees(estimate)}</Text>
        </View>
        <Pressable
          disabled={count === 0}
          onPress={() => router.push('/(app)/booking/pickup')}
          className={`flex-row items-center gap-2 rounded-2xl px-6 py-4 ${count === 0 ? 'bg-cream-300' : 'bg-gold-400'}`}
          accessibilityLabel="Continue to schedule pickup"
        >
          <Text className={`text-base font-extrabold ${count === 0 ? 'text-ink-faint' : 'text-olive-900'}`}>
            Continue
          </Text>
          <Ionicons name="arrow-forward" size={18} color={count === 0 ? '#A8A493' : '#2E351C'} />
        </Pressable>
      </View>
    </SafeAreaView>
  );
}
