/**
 * Booking step 3 — "Payment".
 * Order summary + payment-method picker. On pay:
 *   - FEATURES.bookingApi=true  → calls POST /api/v1/customer/pickup-requests
 *     with slot, address, cart items, and payment preference. Uses the server-
 *     issued requestNumber and id on the confirmation screen.
 *   - FEATURES.bookingApi=false → local fallback (generates a fake LG-##### id
 *     for demo purposes).
 *
 * UPI / card are NOT selectable until the Razorpay native-SDK integration ships.
 * Only Wallet (with balance guard) and Cash on Delivery are live options.
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
import { hapticError, hapticImpact, hapticWarning } from '@/lib/haptics';
import { FEATURES } from '@/constants/config';

const EXPRESS_SURCHARGE = 50;

/** Only the two methods that are live today. */
type LivePaymentMethod = Extract<PaymentMethod, 'wallet' | 'cod'>;

interface MethodMeta {
  key: LivePaymentMethod;
  title: string;
  subtitle: string;
  icon: React.ComponentProps<typeof Ionicons>['name'];
}

/** Map app payment method keys to the backend's payment_preference values. */
function toPaymentPreference(method: LivePaymentMethod): string {
  if (method === 'wallet') return 'wallet';
  return 'cod';
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

  // Coerce any legacy upi/card store state to cod so no silent downgrade persists.
  const liveMethod: LivePaymentMethod =
    paymentMethod === 'wallet' ? 'wallet' : 'cod';

  // Derive values with useMemo — no fresh objects from selectors (zustand v5 rule).
  const lineList = useMemo(() => Object.values(lines), [lines]);
  const count = useMemo(() => lineList.reduce((n, l) => n + l.qty, 0), [lineList]);
  const subtotal = useMemo(() => lineList.reduce((sum, l) => sum + l.qty * l.unitPrice, 0), [lineList]);

  const expressFee = express ? EXPRESS_SURCHARGE : 0;
  const discount = liveMethod === 'wallet' ? Math.round(subtotal * 0.1) : 0;
  const total = Math.max(0, subtotal + expressFee - discount);

  // MOB-4: wallet is disabled when balance < total
  const walletBalance = wallet?.balance ?? 0;
  const walletInsufficient = liveMethod === 'wallet' && walletBalance < total;

  const methods: MethodMeta[] = useMemo(
    () => [
      {
        key: 'wallet',
        title: t('booking.paymentMethods.wallet'),
        subtitle: wallet
          ? t('booking.paymentMethods.walletBalance', { balance: rupees(wallet.balance) })
          : t('booking.paymentMethods.walletDefault'),
        icon: 'wallet',
      },
      {
        key: 'cod',
        title: t('booking.paymentMethods.cod'),
        subtitle: t('booking.paymentMethods.codSubtitle'),
        icon: 'cash-outline',
      },
    ] satisfies MethodMeta[],
    [wallet, t],
  );

  const handlePay = async () => {
    if (count === 0) {
      hapticWarning();
      Alert.alert(t('booking.emptyCart'), t('booking.emptyCartMessage'));
      return;
    }

    hapticImpact();

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

      // Use the slot's stored window times (set by the live slot picker in pickup.tsx).
      // Fall back to a full-day window if somehow missing.
      const [winStart, winEnd] = slot
        ? [slot.windowStart ?? '09:00:00', slot.windowEnd ?? '21:00:00']
        : ['09:00:00', '21:00:00'];

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
          slotId: slot?.id ?? null,  // real UUID from live slot picker
          pickupDate: pickupDateIso,
          pickupWindowStart: winStart,
          pickupWindowEnd: winEnd,
          isExpress: express,
          estimatedItems: count,
          estimatedAmount: total,
          servicesRequested: [],
          customerNotes: null,
          cartItems,
          paymentPreference: toPaymentPreference(liveMethod),
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
          paymentMethod: liveMethod,
        });
        clearCart();
        router.replace('/(app)/booking/confirm');
      } catch (err: unknown) {
        hapticError();
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
        paymentMethod: liveMethod,
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
        <View className="mx-5 gap-3">
          {methods.map((m) => {
            const selected = liveMethod === m.key;
            // Disable wallet when balance is insufficient
            const isWalletDisabled = m.key === 'wallet' && (wallet?.balance ?? 0) < total;
            return (
              <Pressable
                key={m.key}
                onPress={() => {
                  if (!isWalletDisabled) setPaymentMethod(m.key);
                }}
                disabled={isWalletDisabled}
                accessibilityRole="radio"
                accessibilityState={{ selected, disabled: isWalletDisabled }}
                accessibilityLabel={`${m.title}${isWalletDisabled ? ' - ' + t('booking.walletInsufficient') : ''}`}
                className={[
                  'flex-row items-center rounded-2xl border bg-white p-4',
                  selected ? 'border-olive-600' : 'border-cream-300',
                  isWalletDisabled ? 'opacity-50' : '',
                ].join(' ')}
              >
                <View className="mr-3 h-10 w-10 items-center justify-center rounded-xl bg-cream-100">
                  <Ionicons name={m.icon} size={20} color="#5C6A33" />
                </View>
                <View className="flex-1">
                  <Text className="text-base font-bold text-ink">{m.title}</Text>
                  <Text className="text-xs text-ink-muted">
                    {isWalletDisabled ? t('booking.walletInsufficient') : m.subtitle}
                  </Text>
                </View>
                <View className={`h-5 w-5 items-center justify-center rounded-full border-2 ${selected ? 'border-olive-600' : 'border-cream-400'}`}>
                  {selected ? <View className="h-2.5 w-2.5 rounded-full bg-olive-600" /> : null}
                </View>
              </Pressable>
            );
          })}
        </View>

        {/* Coming-soon info row — UPI / Card */}
        <View className="mx-5 mt-3 flex-row items-start gap-3 rounded-2xl border border-cream-200 bg-white px-4 py-3">
          <Ionicons name="time-outline" size={18} color="#A8A493" style={{ marginTop: 1 }} />
          <View className="flex-1">
            <Text className="text-sm font-bold text-ink-muted">{t('booking.onlinePaymentComingSoonTitle')}</Text>
            <Text className="mt-0.5 text-xs leading-4 text-ink-faint">
              {t('booking.onlinePaymentComingSoonBody')}
            </Text>
          </View>
        </View>
      </ScrollView>

      {/* Pay */}
      <View
        className="absolute inset-x-0 bottom-0 border-t border-cream-200 bg-white px-6 pb-8 pt-4"
        style={{ borderTopLeftRadius: 28, borderTopRightRadius: 28 }}
      >
        {walletInsufficient ? (
          <View className="mb-3 flex-row items-center gap-2 rounded-xl bg-red-50 px-4 py-2.5">
            <Ionicons name="alert-circle-outline" size={16} color="#C0492F" />
            <Text className="flex-1 text-xs font-semibold text-danger">
              {t('booking.walletInsufficientAction')}
            </Text>
          </View>
        ) : null}
        <Pressable
          onPress={() => void handlePay()}
          disabled={loading || walletInsufficient}
          className={[
            'flex-row items-center justify-center gap-2 rounded-2xl py-4',
            loading ? 'bg-olive-400' : walletInsufficient ? 'bg-cream-300' : 'bg-olive-700',
          ].join(' ')}
          accessibilityLabel={walletInsufficient ? t('booking.walletInsufficient') : `Pay ${rupees(total)}`}
          accessibilityState={{ disabled: loading || walletInsufficient }}
        >
          <Text className={`text-base font-extrabold ${walletInsufficient ? 'text-ink-faint' : 'text-white'}`}>
            {loading ? t('booking.confirming') : t('booking.pay', { amount: rupees(total) })}
          </Text>
          {!loading && !walletInsufficient ? <Ionicons name="arrow-forward" size={18} color="#FFFFFF" /> : null}
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
