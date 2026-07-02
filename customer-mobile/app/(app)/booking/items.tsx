/**
 * Booking step 1 — "What needs washing?"
 * Item picker with quantity steppers, built on the live price list
 * (GET {Catalog}/customer/catalog/price-list) with a demo fallback so the
 * flow always works in dev. Selection lives in the cart store.
 *
 * GH #22: value-slab items (branded/luxury garments priced by declared value) carry a
 * "Priced by garment value" badge. Adding one opens a bottom sheet to collect the
 * customer's declared value, which is stored on the cart line and sent at pickup. Their
 * placeholder price is excluded from the estimate (resolved server-side from the value).
 */
import React, { useMemo, useState } from 'react';
import { FlatList, Pressable, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import { useCatalogConfig, usePriceList } from '@/hooks/useCatalog';
import { useCartStore, type CartLine } from '@/store/cartStore';
import { useBookingStore } from '@/store/bookingStore';
import { Stepper } from '@/components/ui/Stepper';
import { Chip } from '@/components/ui/Chip';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { DeclaredValueSheet } from '@/components/ui/DeclaredValueSheet';
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
  /** GH #22 — 'value_slab' items are priced from a declared value, not the listed rate. */
  pricingMode: 'standard' | 'value_slab';
}

/** Build the cart-line metadata (everything but qty) from a picker row. */
function toCartMeta(item: PickerItem): Omit<CartLine, 'qty'> {
  return {
    id: item.id,
    itemId: item.itemId,
    serviceId: item.serviceId,
    name: item.label,
    service: item.fabric,
    unitPrice: item.unitPrice,
    pricingMode: item.pricingMode,
  };
}

interface ItemRowProps {
  item: PickerItem;
  /** Open the declared-value sheet for a value-slab item (add or edit). */
  onRequestValue: (item: PickerItem, initial?: number) => void;
}

function ItemRow({ item, onRequestValue }: ItemRowProps) {
  const { t } = useTranslation();
  const lines = useCartStore((s) => s.lines);
  const setQty = useCartStore((s) => s.setQty);
  const line = lines[item.id];
  const qty = line?.qty ?? 0;
  const declaredValue = line?.declaredValue;
  const isValueSlab = item.pricingMode === 'value_slab';

  return (
    <View className="border-b border-cream-200 py-3.5">
      <View className="flex-row items-center">
        <View className="mr-3 h-11 w-11 items-center justify-center rounded-xl bg-cream-100">
          <Ionicons name="shirt-outline" size={20} color="#5C6A33" />
        </View>
        <View className="flex-1">
          <Text className="text-base font-bold text-ink">{item.name}</Text>
          {isValueSlab ? (
            <View className="mt-1 flex-row items-center self-start gap-1 rounded-full bg-gold-100 px-2 py-0.5">
              <Ionicons name="diamond-outline" size={11} color="#8A641D" />
              <Text className="text-[11px] font-bold text-gold-700">
                {t('booking.valueSlab.badge')}
              </Text>
            </View>
          ) : (
            <Text className="text-xs text-ink-muted">
              {item.fabric} · {t('booking.perPiece', { price: rupees(item.unitPrice) })}
            </Text>
          )}
        </View>
        <Stepper
          value={qty}
          onChange={(next) => {
            // GH #22: first add of a value-slab item must collect its declared value
            // before the line is committed to the cart.
            if (isValueSlab && qty === 0 && next > 0) {
              onRequestValue(item, undefined);
              return;
            }
            setQty(toCartMeta(item), next);
          }}
        />
      </View>

      {/* Declared-value line for a value-slab item already in the cart. */}
      {isValueSlab && qty > 0 ? (
        <Pressable
          onPress={() => onRequestValue(item, declaredValue)}
          className="mt-2.5 ml-14 flex-row items-center gap-1.5"
          accessibilityRole="button"
          accessibilityLabel={t('booking.valueSlab.changeValue')}
        >
          <Ionicons name="pricetag-outline" size={13} color="#5C6A33" />
          <Text className="text-xs font-semibold text-olive-700">
            {declaredValue != null
              ? t('booking.valueSlab.declaredLine', { value: rupees(declaredValue) })
              : t('booking.valueSlab.needsValue')}
          </Text>
          <Text className="text-xs font-bold text-olive-600">· {t('booking.valueSlab.change')}</Text>
        </Pressable>
      ) : null}
    </View>
  );
}

export default function ItemsScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const { data: priceList, isLoading } = usePriceList();
  const { data: catalogConfig } = useCatalogConfig();
  const cartLines = useCartStore((s) => s.lines);
  const count = useCartStore((s) => s.count());
  const subtotal = useCartStore((s) => s.subtotal());
  const setQty = useCartStore((s) => s.setQty);
  const clearCart = useCartStore((s) => s.clear);
  const express = useBookingStore((s) => s.express);
  const setExpress = useBookingStore((s) => s.setExpress);
  const resetBooking = useBookingStore((s) => s.reset);
  const confirmed = useBookingStore((s) => s.confirmed);

  // GH #22: declared-value sheet target (the item being added / edited).
  const [valueSheet, setValueSheet] = useState<{ item: PickerItem; initial?: number } | null>(null);

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
          pricingMode: p.pricingMode === 'value_slab' ? 'value_slab' : 'standard',
        } satisfies PickerItem;
      });
    if (live.length > 0) return live;
    return DEMO_ITEMS.map((d) => ({
      ...d,
      itemId: null,
      serviceId: null,
      label: `${d.name} · ${d.fabric}`,
      pricingMode: 'standard' as const,
    }));
  }, [priceList]);

  // Express surcharge mirrors the mockup (+₹50 flat shown later); here we only
  // tag the booking. Estimate total = subtotal (+ express handled at payment).
  const estimate = subtotal;

  // GH #22: any value-slab lines in the cart are priced later, so the estimate
  // above excludes them — surface a small note so ₹0 / a low total isn't confusing.
  const hasValueSlab = useMemo(
    () => Object.values(cartLines).some((l) => l.pricingMode === 'value_slab'),
    [cartLines],
  );

  // #23: slim min-order progress hint so the blocking sheet at Pay isn't a surprise.
  // Suppressed while value-slab lines are present — their price is unknown client-side,
  // so the true order total can't be evaluated here (the server is the authority).
  const minGate = useMemo(() => evaluateMinOrder(subtotal, catalogConfig), [subtotal, catalogConfig]);
  const showMinHint = (minGate?.blocked ?? false) && count > 0 && !hasValueSlab;

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
        renderItem={({ item }) => <ItemRow item={item} onRequestValue={(it, initial) => setValueSheet({ item: it, initial })} />}
        contentContainerStyle={{ paddingHorizontal: 20, paddingBottom: 140 }}
        showsVerticalScrollIndicator={false}
      />

      {/* Total bar */}
      <View
        className="absolute inset-x-0 bottom-0 border-t border-cream-200 bg-white px-6 pb-8 pt-4"
        style={{ borderTopLeftRadius: 28, borderTopRightRadius: 28 }}
      >
        {showMinHint && minGate ? (
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
          <View className="flex-1 pr-3">
            <Text className="text-xs text-ink-muted">
              {t('booking.itemCount', { count, plural: count !== 1 ? 's' : '' })}{express ? ` · ${t('booking.express')}` : ''}
            </Text>
            <Text className="text-2xl font-extrabold text-ink">{rupees(estimate)}</Text>
            {hasValueSlab ? (
              <Text className="text-[11px] font-semibold text-gold-700">
                {t('booking.valueSlab.estimateNote')}
              </Text>
            ) : null}
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

      {/* GH #22: declared-value bottom sheet */}
      {valueSheet ? (
        <DeclaredValueSheet
          visible
          currencyCode={catalogConfig?.currencyCode ?? 'INR'}
          itemName={valueSheet.item.name}
          threshold={catalogConfig?.highValueGarmentThreshold ?? null}
          initialValue={valueSheet.initial}
          onSubmit={(value) => {
            const existing = useCartStore.getState().lines[valueSheet.item.id];
            // Merge declaredValue into the line, creating it at qty 1 on first add.
            setQty(
              { ...toCartMeta(valueSheet.item), declaredValue: value },
              existing ? existing.qty : 1,
            );
            setValueSheet(null);
          }}
          onClose={() => setValueSheet(null)}
        />
      ) : null}
    </SafeAreaView>
  );
}
