/**
 * Booking step 3 — "Payment".
 * Order summary + payment-method picker. On pay:
 *   - FEATURES.bookingApi=true  → calls POST /api/v1/customer/pickup-requests
 *     with slot, address, cart items, and payment preference. Uses the server-
 *     issued requestNumber and id on the confirmation screen.
 *   - FEATURES.bookingApi=false → local fallback (generates a fake LG-##### id
 *     for demo purposes).
 *
 * UPI/card selections fall back to "upi-deferred" server-side, and the
 * confirmation shows "Pay on delivery". Razorpay native SDK is a separate task.
 */
import React, { useMemo, useState } from 'react';
import { Alert, Pressable, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import { useCartStore } from '@/store/cartStore';
import { useBookingStore, type PaymentMethod } from '@/store/bookingStore';
import { useWallet } from '@/hooks/useCommerce';
import { useSchedulePickup } from '@/hooks/useOrders';
import { rupees } from '@/lib/format';
import { FEATURES } from '@/constants/config';

const EXPRESS_SURCHARGE = 50;

interface MethodMeta {
  key: PaymentMethod;
  title: string;
  subtitle: string;
  icon: React.ComponentProps<typeof Ionicons>['name'];
}

/** Map app payment method keys to the backend's payment_preference values. */
function toPaymentPreference(method: PaymentMethod): string {
  if (method === 'wallet') return 'wallet';
  if (method === 'cod') return 'cod';
  // upi / card → upi-deferred (no native SDK yet; collected at delivery)
  return 'upi-deferred';
}

export default function PayScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const { dateLabel } = useLocalSearchParams<{ dateLabel?: string }>();

  // Select raw state scalars — never fresh objects/arrays from selector (zustand v5 rule).
  const lines = useCartStore((s) => s.lines);
  const clearCart = useCartStore((s) => s.clear);
  const { express, slot, address, paymentMethod, setPaymentMethod, setConfirmed } = useBookingStore();
  const { data: wallet } = useWallet();

  const [loading, setLoading] = useState(false);
  const schedulePickup = useSchedulePickup();

  // Derive values with useMemo — no fresh objects from selectors (zustand v5 rule).
  const lineList = useMemo(() => Object.values(lines), [lines]);
  const count = useMemo(() => lineList.reduce((n, l) => n + l.qty, 0), [lineList]);
  const subtotal = useMemo(() => lineList.reduce((sum, l) => sum + l.qty * l.unitPrice, 0), [lineList]);

  const expressFee = express ? EXPRESS_SURCHARGE : 0;
  const discount = paymentMethod === 'wallet' ? Math.round(subtotal * 0.1) : 0;
  const total = Math.max(0, subtotal + expressFee - discount);

  const methods: MethodMeta[] = useMemo(
    () => [
      { key: 'wallet', title: t('booking.paymentMethods.wallet'), subtitle: wallet ? t('booking.paymentMethods.walletBalance', { balance: rupees(wallet.balance) }) : t('booking.paymentMethods.walletDefault'), icon: 'wallet' },
      { key: 'upi',    title: t('booking.paymentMethods.upi'),    subtitle: t('booking.paymentMethods.upiSubtitle'),    icon: 'phone-portrait-outline' },
      { key: 'card',   title: t('booking.paymentMethods.card'),   subtitle: t('booking.paymentMethods.cardSubtitle'),   icon: 'card-outline' },
      { key: 'cod',    title: t('booking.paymentMethods.cod'),    subtitle: t('booking.paymentMethods.codSubtitle'),    icon: 'cash-outline' },
    ],
    [wallet, t],
  );

  const handlePay = async () => {
    if (count === 0) {
      Alert.alert(t('booking.emptyCart'), t('booking.emptyCartMessage'));
      return;
    }

    if (FEATURES.bookingApi) {
      // ── Live API path ───────────────────────────────────────────────────────
      if (!address?.id) {
        Alert.alert(t('booking.noAddress'), t('booking.noAddressMessage'));
        return;
      }

      // Build a "today" date if the slot doesn't carry date info (slot picker
      // only stores a label; full date comes from the day picker state in pickup.tsx
      // via the slot.date field on BookingSlot).
      const pickupDateIso = slot?.date ?? new Date().toISOString().slice(0, 10);

      // Parse window start/end from the slot label (e.g. "12 – 2 PM").
      // The static slot list in pickup.tsx uses fixed ids (s-10, s-12, …) that
      // encode the start hour. Fall back to 09:00/21:00 for unknown ids.
      const slotHourMap: Record<string, [string, string]> = {
        's-10': ['10:00:00', '12:00:00'],
        's-12': ['12:00:00', '14:00:00'],
        's-14': ['14:00:00', '16:00:00'],
        's-16': ['16:00:00', '18:00:00'],
        's-18': ['18:00:00', '20:00:00'],
        's-20': ['20:00:00', '22:00:00'],
      };
      const [winStart, winEnd] = slot ? (slotHourMap[slot.id] ?? ['09:00:00', '21:00:00']) : ['09:00:00', '21:00:00'];

      // Build estimated cart items from the cart store lines.
      const cartItems = lineList.map((l) => ({
        serviceId: null,
        itemId: l.id.startsWith('demo-') ? null : l.id,  // demo items have no catalog id
        displayLabel: `${l.name} · ${l.service}`,
        quantity: l.qty,
        estimatedUnitPrice: l.unitPrice,
      }));

      setLoading(true);
      try {
        const result = await schedulePickup.mutateAsync({
          addressId: address.id,
          slotId: slot?.id?.startsWith('s-') ? null : (slot?.id ?? null),  // local static ids are not DB ids
          pickupDate: pickupDateIso,
          pickupWindowStart: winStart,
          pickupWindowEnd: winEnd,
          isExpress: express,
          estimatedItems: count,
          estimatedAmount: total,
          servicesRequested: [],
          customerNotes: null,
          cartItems,
          paymentPreference: toPaymentPreference(paymentMethod),
        });

        setConfirmed({
          orderNumber: result.requestNumber,
          pickupRequestId: result.id,
          address: address.line1,
          slotLabel: slot?.label ?? `${winStart.slice(0, 5)} – ${winEnd.slice(0, 5)}`,
          dateLabel: dateLabel ?? pickupDateIso,
          itemCount: count,
          express,
          amount: total,
          paymentMethod,
        });
        clearCart();
        router.replace('/(app)/booking/confirm');
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : t('booking.bookingFailedMessage');
        Alert.alert(t('booking.bookingFailed'), msg);
      } finally {
        setLoading(false);
      }
    } else {
      // ── Local fallback (FEATURES.bookingApi=false) ──────────────────────────
      const orderNumber = `LG-${Math.floor(10000 + Math.random() * 90000)}`;
      setConfirmed({
        orderNumber,
        address: address?.line1 ?? 'DLF Phase 4',
        slotLabel: slot?.label ?? '12 – 2 PM',
        dateLabel: dateLabel ?? 'Today',
        itemCount: count,
        express,
        amount: total,
        paymentMethod,
      });
      clearCart();
      router.replace('/(app)/booking/confirm');
    }
  };

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
        <Text className="text-xl font-extrabold text-ink">{t('booking.payment')}</Text>
      </View>

      <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 130 }}>
        {/* Summary */}
        <View className="mx-5 rounded-2xl bg-white p-4">
          <Text className="mb-3 text-[11px] font-bold uppercase tracking-wider text-ink-faint">{t('booking.orderSummary')}</Text>
          <SummaryRow label={t('booking.garmentCount', { count, plural: count !== 1 ? 's' : '' })} value={rupees(subtotal)} />
          {express ? <SummaryRow label={t('booking.expressPlus')} value={rupees(expressFee)} /> : null}
          {discount > 0 ? <SummaryRow label={t('booking.goldPackDiscount')} value={`−${rupees(discount)}`} accent /> : null}
          <View className="mt-2 flex-row items-center justify-between border-t border-cream-200 pt-3">
            <Text className="text-base font-extrabold text-ink">{t('booking.totalToPay')}</Text>
            <Text className="text-base font-extrabold text-ink">{rupees(total)}</Text>
          </View>
        </View>

        {/* Methods */}
        <Text className="mx-5 mb-3 mt-7 text-[11px] font-bold uppercase tracking-wider text-ink-faint">{t('booking.payWith')}</Text>
        {(paymentMethod === 'upi' || paymentMethod === 'card') ? (
          <View className="mx-5 mb-2 rounded-xl bg-gold-100 px-4 py-2">
            <Text className="text-xs text-gold-700">
              {t('booking.upiCardNote')}
            </Text>
          </View>
        ) : null}
        <View className="mx-5 gap-3">
          {methods.map((m) => {
            const selected = paymentMethod === m.key;
            return (
              <Pressable
                key={m.key}
                onPress={() => setPaymentMethod(m.key)}
                className={`flex-row items-center rounded-2xl border bg-white p-4 ${selected ? 'border-olive-600' : 'border-cream-300'}`}
              >
                <View className="mr-3 h-10 w-10 items-center justify-center rounded-xl bg-cream-100">
                  <Ionicons name={m.icon} size={20} color="#5C6A33" />
                </View>
                <View className="flex-1">
                  <Text className="text-base font-bold text-ink">{m.title}</Text>
                  <Text className="text-xs text-ink-muted">{m.subtitle}</Text>
                </View>
                <View className={`h-5 w-5 items-center justify-center rounded-full border-2 ${selected ? 'border-olive-600' : 'border-cream-400'}`}>
                  {selected ? <View className="h-2.5 w-2.5 rounded-full bg-olive-600" /> : null}
                </View>
              </Pressable>
            );
          })}
        </View>
      </ScrollView>

      {/* Pay */}
      <View
        className="absolute inset-x-0 bottom-0 border-t border-cream-200 bg-white px-6 pb-8 pt-4"
        style={{ borderTopLeftRadius: 28, borderTopRightRadius: 28 }}
      >
        <Pressable
          onPress={() => void handlePay()}
          disabled={loading}
          className={`flex-row items-center justify-center gap-2 rounded-2xl py-4 ${loading ? 'bg-olive-400' : 'bg-olive-700'}`}
          accessibilityLabel={`Pay ${rupees(total)}`}
        >
          <Text className="text-base font-extrabold text-white">
            {loading ? t('booking.confirming') : t('booking.pay', { amount: rupees(total) })}
          </Text>
          {!loading ? <Ionicons name="arrow-forward" size={18} color="#FFFFFF" /> : null}
        </Pressable>
      </View>
    </SafeAreaView>
  );
}

function SummaryRow({ label, value, accent }: { label: string; value: string; accent?: boolean }) {
  return (
    <View className="flex-row items-center justify-between py-1.5">
      <Text className="text-sm text-ink-muted">{label}</Text>
      <Text className={`text-sm font-bold ${accent ? 'text-success' : 'text-ink-soft'}`}>{value}</Text>
    </View>
  );
}
