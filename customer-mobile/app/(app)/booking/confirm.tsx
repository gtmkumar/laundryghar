/**
 * Booking step 4 — "Pickup scheduled!" confirmation.
 * Reads the confirmed booking from the booking store. Offers "Back to home"
 * and "View order" (opens the live tracking timeline).
 */
import React from 'react';
import { Pressable, Text, View } from 'react-native';
import { Redirect, useRouter } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import { StatusBar } from 'expo-status-bar';
import { LinearGradient } from 'expo-linear-gradient';
import { Ionicons } from '@expo/vector-icons';
import { useBookingStore } from '@/store/bookingStore';
import { rupees } from '@/lib/format';

const PAYMENT_LABEL: Record<string, string> = {
  wallet: 'Wallet',
  upi: 'UPI',
  card: 'Card',
  cod: 'Cash on Delivery',
};

function DetailRow({ label, value, accent }: { label: string; value: string; accent?: boolean }) {
  return (
    <View className="flex-row items-center justify-between py-2.5">
      <Text className="text-sm text-ink-muted">{label}</Text>
      <Text className={`text-sm font-bold ${accent ? 'text-gold-700' : 'text-ink'}`}>{value}</Text>
    </View>
  );
}

export default function ConfirmScreen() {
  const router = useRouter();
  const confirmed = useBookingStore((s) => s.confirmed);

  if (!confirmed) {
    return <Redirect href="/(app)/(tabs)/home" />;
  }

  const paid = confirmed.paymentMethod === 'cod'
    ? `${rupees(confirmed.amount)} · COD`
    : `${rupees(confirmed.amount)} · ${PAYMENT_LABEL[confirmed.paymentMethod]}`;

  return (
    <View className="flex-1 bg-cream">
      <StatusBar style="light" />
      {/* Success header */}
      <LinearGradient colors={['#73803F', '#5C6A33']} style={{ paddingTop: 64, paddingBottom: 40 }}>
        <SafeAreaView edges={['top']}>
          <View className="items-center px-8">
            <View className="h-20 w-20 items-center justify-center rounded-full bg-olive-800">
              <Ionicons name="checkmark" size={44} color="#FFFFFF" />
            </View>
            <Text className="mt-5 text-3xl font-extrabold text-white">Pickup scheduled!</Text>
            <Text className="mt-2 text-center text-base text-olive-100">
              A rider will collect from your door.
            </Text>
          </View>
        </SafeAreaView>
      </LinearGradient>

      <View className="flex-1 px-6">
        {/* Details card */}
        <View className="-mt-6 rounded-3xl bg-white p-5" style={{
          shadowColor: '#2E351C', shadowOpacity: 0.08, shadowRadius: 14, shadowOffset: { width: 0, height: 6 }, elevation: 4,
        }}>
          <DetailRow label="Order" value={`#${confirmed.orderNumber}`} />
          <View className="h-px bg-cream-200" />
          <DetailRow label="Pickup window" value={`${confirmed.dateLabel} · ${confirmed.slotLabel}`} />
          <DetailRow label="Address" value={confirmed.address} />
          <DetailRow label="Items" value={`${confirmed.itemCount} garment${confirmed.itemCount !== 1 ? 's' : ''}`} />
          {confirmed.express ? <DetailRow label="Express" value="+₹50" accent /> : null}
          <View className="h-px bg-cream-200" />
          <DetailRow label="Paid" value={paid} />
        </View>

        {/* Note */}
        <View className="mt-4 flex-row items-start gap-2 rounded-2xl bg-cream-100 p-4">
          <Ionicons name="logo-whatsapp" size={18} color="#4F8A4F" />
          <Text className="flex-1 text-xs leading-5 text-ink-muted">
            We’ll WhatsApp you when the rider is 10 minutes away. Garment-by-garment
            tracking starts after pickup.
          </Text>
        </View>

        <View className="flex-1" />

        {/* Actions */}
        <View className="flex-row gap-3 pb-8">
          <Pressable
            onPress={() => router.replace('/(app)/(tabs)/home')}
            className="flex-1 items-center justify-center rounded-2xl border border-olive-300 py-4"
          >
            <Text className="text-base font-bold text-olive-800">Back to home</Text>
          </Pressable>
          <Pressable
            onPress={() => router.replace(`/(app)/orders/tracking/${confirmed.orderNumber}`)}
            className="flex-1 flex-row items-center justify-center gap-2 rounded-2xl bg-gold-400 py-4"
          >
            <Text className="text-base font-extrabold text-olive-900">View order</Text>
            <Ionicons name="arrow-forward" size={16} color="#2E351C" />
          </Pressable>
        </View>
      </View>
    </View>
  );
}
