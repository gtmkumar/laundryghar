/**
 * Booking step 3 — "Payment".
 * Order summary + payment-method picker. On pay, finalises the booking.
 *
 * There's no single customer "create order with items" endpoint yet
 * (FEATURES.bookingApi=false), so this finalises locally: it records a
 * confirmed booking and routes to the confirmation. Razorpay (UPI/card) needs
 * a native SDK that isn't wired, so those settle as "pay on delivery" for now.
 */
import React, { useMemo } from 'react';
import { Alert, Pressable, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useCartStore } from '@/store/cartStore';
import { useBookingStore, type PaymentMethod } from '@/store/bookingStore';
import { useWallet } from '@/hooks/useCommerce';
import { rupees } from '@/lib/format';

const EXPRESS_SURCHARGE = 50;

interface MethodMeta {
  key: PaymentMethod;
  title: string;
  subtitle: string;
  icon: React.ComponentProps<typeof Ionicons>['name'];
}

export default function PayScreen() {
  const router = useRouter();
  const { dateLabel } = useLocalSearchParams<{ dateLabel?: string }>();
  const subtotal = useCartStore((s) => s.subtotal());
  const count = useCartStore((s) => s.count());
  const clearCart = useCartStore((s) => s.clear);
  const { express, slot, address, paymentMethod, setPaymentMethod, setConfirmed } = useBookingStore();
  const { data: wallet } = useWallet();

  const expressFee = express ? EXPRESS_SURCHARGE : 0;
  const discount = paymentMethod === 'wallet' ? Math.round(subtotal * 0.1) : 0;
  const total = Math.max(0, subtotal + expressFee - discount);

  const methods: MethodMeta[] = useMemo(
    () => [
      { key: 'wallet', title: 'Wallet – Gold pack', subtitle: wallet ? `Balance ${rupees(wallet.balance)}` : 'Pay from wallet', icon: 'wallet' },
      { key: 'upi',    title: 'UPI',                 subtitle: 'GPay, PhonePe, Paytm',    icon: 'phone-portrait-outline' },
      { key: 'card',   title: 'Credit / Debit card', subtitle: 'Visa, Mastercard, Rupay', icon: 'card-outline' },
      { key: 'cod',    title: 'Cash on Delivery',    subtitle: 'Pay rider on return',     icon: 'cash-outline' },
    ],
    [wallet],
  );

  const handlePay = () => {
    if (count === 0) {
      Alert.alert('Empty cart', 'Add at least one item to continue.');
      return;
    }
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
  };

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
        <Text className="text-xl font-extrabold text-ink">Payment</Text>
      </View>

      <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 130 }}>
        {/* Summary */}
        <View className="mx-5 rounded-2xl bg-white p-4">
          <Text className="mb-3 text-[11px] font-bold uppercase tracking-wider text-ink-faint">Order summary</Text>
          <SummaryRow label={`${count} garment${count !== 1 ? 's' : ''} · clean`} value={rupees(subtotal)} />
          {express ? <SummaryRow label="Express +" value={rupees(expressFee)} /> : null}
          {discount > 0 ? <SummaryRow label="Gold pack discount" value={`−${rupees(discount)}`} accent /> : null}
          <View className="mt-2 flex-row items-center justify-between border-t border-cream-200 pt-3">
            <Text className="text-base font-extrabold text-ink">Total to pay</Text>
            <Text className="text-base font-extrabold text-ink">{rupees(total)}</Text>
          </View>
        </View>

        {/* Methods */}
        <Text className="mx-5 mb-3 mt-7 text-[11px] font-bold uppercase tracking-wider text-ink-faint">Pay with</Text>
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
          onPress={handlePay}
          className="flex-row items-center justify-center gap-2 rounded-2xl bg-olive-700 py-4"
          accessibilityLabel={`Pay ${rupees(total)}`}
        >
          <Text className="text-base font-extrabold text-white">Pay {rupees(total)}</Text>
          <Ionicons name="arrow-forward" size={18} color="#FFFFFF" />
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
