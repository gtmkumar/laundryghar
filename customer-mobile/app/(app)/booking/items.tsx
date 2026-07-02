/**
 * Booking step 1 — "What needs washing?"
 * Item picker with quantity steppers, built on the live price list
 * (GET {Catalog}/customer/catalog/price-list) with a demo fallback so the
 * flow always works in dev. Selection lives in the cart store.
 */
import React, { useMemo } from 'react';
import { FlatList, Pressable, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import { useCatalogConfig, usePriceList } from '@/hooks/useCatalog';
import { useCartStore } from '@/store/cartStore';
import { useBookingStore } from '@/store/bookingStore';
import { Stepper } from '@/components/ui/Stepper';
import { Chip } from '@/components/ui/Chip';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { rupees } from '@/lib/format';
import { evaluateMinOrder, formatCurrency } from '@/lib/minOrder';
import { DEMO_ITEMS } from '@/data/demoItems';

interface PickerItem {
  /** Price-list ROW id — local cart key only, never sent as itemId. */
  id: string;
  /** Real catalog ids from the price-list entry (null for demo items). */
  itemId: string | null;
  serviceId: string | null;
  /** Row title, e.g. "Shirt". */
  name: string;
  /** Sub-label, e.g. the service name. */
  fabric: string;
  /** Full display label sent to the API, e.g. "Shirt · Wash & Iron". */
  label: string;
  unitPrice: number;
}

function ItemRow({ item }: { item: PickerItem }) {
  const { t } = useTranslation();
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
          {item.fabric} · {t('booking.perPiece', { price: rupees(item.unitPrice) })}
        </Text>
      </View>
      <Stepper
        value={qty}
        onChange={(next) =>
          setQty(
            {
              id: item.id,
              itemId: item.itemId,
              serviceId: item.serviceId,
              name: item.label,
              service: item.fabric,
              unitPrice: item.unitPrice,
            },
            next,
          )
        }
      />
    </View>
  );
}

export default function ItemsScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const { data: priceList, isLoading } = usePriceList();
  const { data: catalogConfig } = useCatalogConfig();
  const count = useCartStore((s) => s.count());
  const subtotal = useCartStore((s) => s.subtotal());
  const clearCart = useCartStore((s) => s.clear);
  const express = useBookingStore((s) => s.express);
  const setExpress = useBookingStore((s) => s.setExpress);
  const resetBooking = useBookingStore((s) => s.reset);
  const confirmed = useBookingStore((s) => s.confirmed);

  // MOB-8: if arriving with a prior confirmed booking (user tapped "Back to Home"
  // then started a new booking), clear the confirmed state and cart.
  React.useEffect(() => {
    if (confirmed) {
      resetBooking();
      clearCart();
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const items: PickerItem[] = useMemo(() => {
    const live = (priceList ?? [])
      .filter((p) => p.isActive)
      .map((p) => {
        // Backend now returns itemName/serviceName/displayLabel on price-list rows.
        const joined = [p.itemName, p.serviceName].filter(Boolean).join(' · ');
        const label = p.displayLabel ?? (joined || p.notes || 'Garment');
        return {
          id: p.id,
          itemId: p.itemId ?? null,
          serviceId: p.serviceId ?? null,
          name: p.itemName ?? label,
          fabric: p.serviceName ?? p.notes ?? 'Standard',
          label,
          unitPrice: p.basePrice,
        };
      });
    if (live.length > 0) return live;
    return DEMO_ITEMS.map((d) => ({
      ...d,
      itemId: null,
      serviceId: null,
      label: `${d.name} · ${d.fabric}`,
    }));
  }, [priceList]);

  // Express surcharge mirrors the mockup (+₹50 flat shown later); here we only
  // tag the booking. Estimate total = subtotal (+ express handled at payment).
  const estimate = subtotal;

  // #23: slim min-order progress hint so the blocking sheet at Pay isn't a surprise.
  const minGate = useMemo(() => evaluateMinOrder(subtotal, catalogConfig), [subtotal, catalogConfig]);

  if (isLoading) return <ScreenLoader />;

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      {/* Header */}
      <View className="flex-row items-center gap-3 px-5 pt-2 pb-3">
        <Pressable
          onPress={() => router.back()}
          className="h-10 w-10 items-center justify-center rounded-full bg-white"
          accessibilityRole="button"
          accessibilityLabel={t('a11y.back')}
        >
          <Ionicons name="chevron-back" size={22} color="#3C3F35" />
        </Pressable>
        <Text className="text-xl font-extrabold text-ink">{t('booking.whatNeedsWashing')}</Text>
      </View>

      {/* Filter chips — only the express toggle; Dry Clean chip removed (MOB-12: was dead/misleading) */}
      <View className="flex-row px-5 pb-2">
        <Chip
          label={t('booking.express')}
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
        className="absolute inset-x-0 bottom-0 border-t border-cream-200 bg-white px-6 pb-8 pt-4"
        style={{ borderTopLeftRadius: 28, borderTopRightRadius: 28 }}
      >
        {minGate?.blocked && count > 0 ? (
          <View className="mb-3 flex-row items-center gap-2 rounded-xl bg-gold-100 px-3 py-2">
            <Ionicons name="information-circle-outline" size={15} color="#8A641D" />
            <Text className="flex-1 text-xs font-semibold text-gold-700">
              {t('booking.minOrder.hint', {
                minimum: formatCurrency(minGate.minOrderValue, minGate.currencyCode),
                shortfall: formatCurrency(minGate.shortfall, minGate.currencyCode),
              })}
            </Text>
          </View>
        ) : null}
        <View className="flex-row items-center justify-between">
          <View>
            <Text className="text-xs text-ink-muted">
              {t('booking.itemCount', { count, plural: count !== 1 ? 's' : '' })}{express ? ` · ${t('booking.express')}` : ''}
            </Text>
            <Text className="text-2xl font-extrabold text-ink">{rupees(estimate)}</Text>
          </View>
          <Pressable
            disabled={count === 0}
            onPress={() => router.push('/(app)/booking/pickup')}
            className={`flex-row items-center gap-2 rounded-2xl px-6 py-4 ${count === 0 ? 'bg-cream-300' : 'bg-gold-400'}`}
            accessibilityLabel={t('booking.continueToPickup')}
          >
            <Text className={`text-base font-extrabold ${count === 0 ? 'text-ink-faint' : 'text-olive-900'}`}>
              {t('common.continue')}
            </Text>
            <Ionicons name="arrow-forward" size={18} color={count === 0 ? '#A8A493' : '#2E351C'} />
          </Pressable>
        </View>
      </View>
    </SafeAreaView>
  );
}
