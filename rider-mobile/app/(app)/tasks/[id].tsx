/**
 * Task detail — delivery navigation + OTP confirm (mockup: "Delivering …").
 *
 * Delivery: rider asks the customer for their 4-digit code, enters it, and
 * confirms — we validate against the task's deliveryOtp (the order's
 * delivery_otp once the backend exposes it), then record completion and show
 * the success screen.
 * Pickup: no OTP — just "Confirm pickup".
 * Completed tasks open read-only.
 *
 * The map is a stylised placeholder; a live map (react-native-maps) needs a
 * dev build + key and is tracked as a follow-up.
 */
import React, { useState } from 'react';
import { Linking, Pressable, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons, MaterialCommunityIcons } from '@expo/vector-icons';
import { StatusBar } from 'expo-status-bar';
import { useRiderTask } from '@/hooks/useRiderTasks';
import { useTaskOverrideStore } from '@/store/taskOverrideStore';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { Button } from '@/components/ui/Button';
import { OtpInput } from '@/components/ui/OtpInput';
import { Avatar } from '@/components/ui/Avatar';

function MapPreview({ km, eta }: { km: number; eta?: number }) {
  return (
    <View className="overflow-hidden rounded-3xl bg-olive-100" style={{ height: 200 }}>
      {/* faint grid */}
      <View className="absolute inset-0">
        {[1, 2, 3].map((i) => (
          <View key={`h${i}`} style={{ position: 'absolute', top: i * 50, left: 0, right: 0, height: 1, backgroundColor: '#FFFFFF66' }} />
        ))}
        {[1, 2, 3, 4].map((i) => (
          <View key={`v${i}`} style={{ position: 'absolute', left: i * 64, top: 0, bottom: 0, width: 1, backgroundColor: '#FFFFFF66' }} />
        ))}
      </View>
      {/* route segments */}
      <View style={{ position: 'absolute', left: 40, top: 150, width: 120, height: 3, backgroundColor: '#4A552A', borderRadius: 2 }} />
      <View style={{ position: 'absolute', left: 158, top: 70, width: 3, height: 83, backgroundColor: '#4A552A', borderRadius: 2 }} />
      <View style={{ position: 'absolute', left: 158, top: 70, width: 110, height: 3, backgroundColor: '#4A552A', borderRadius: 2 }} />
      {/* origin */}
      <View style={{ position: 'absolute', left: 32, top: 142, width: 18, height: 18, borderRadius: 9, borderWidth: 4, borderColor: '#3B4423', backgroundColor: '#FFFFFF' }} />
      {/* destination */}
      <View style={{ position: 'absolute', left: 256, top: 56, width: 26, height: 26, borderRadius: 13, backgroundColor: '#DBAC3D', alignItems: 'center', justifyContent: 'center' }}>
        <Ionicons name="location" size={16} color="#3B4423" />
      </View>
      {/* distance pill */}
      <View className="absolute left-3 top-3 flex-row items-center gap-1.5 rounded-full bg-white px-3 py-1.5" style={{ shadowColor: '#000', shadowOpacity: 0.1, shadowRadius: 4 }}>
        <Ionicons name="navigate" size={13} color="#4A552A" />
        <Text className="text-xs font-bold text-ink">{km} km{eta ? ` · ${eta} min` : ''}</Text>
      </View>
    </View>
  );
}

export default function TaskDetailScreen() {
  const router = useRouter();
  const { id } = useLocalSearchParams<{ id: string }>();
  const { task, isLoading } = useRiderTask(id);
  const complete = useTaskOverrideStore((s) => s.complete);

  const [code, setCode] = useState('');
  const [otpError, setOtpError] = useState(false);

  if (isLoading) return <ScreenLoader />;
  if (!task) return <ErrorState message="This task could not be found." />;

  const isDelivery  = task.legType === 'delivery';
  const isCompleted = task.status === 'completed';
  const needsOtp    = isDelivery && !!task.deliveryOtp && !isCompleted;
  const title = isCompleted
    ? `#${task.orderNumber}`
    : `${isDelivery ? 'Delivering' : 'Picking up'} #${task.orderNumber}`;

  function confirm() {
    if (!task) return;
    if (needsOtp) {
      if (code !== task.deliveryOtp) {
        setOtpError(true);
        setCode('');
        return;
      }
    }
    complete(task.id);
    router.replace(`/(app)/delivered?id=${task.id}`);
  }

  const canConfirm = !needsOtp || code.length === 4;

  return (
    <View className="flex-1 bg-cream">
      <StatusBar style="dark" />
      <SafeAreaView className="flex-1" edges={['top', 'left', 'right']}>
        {/* Header */}
        <View className="flex-row items-center px-4 pb-2 pt-1">
          <Pressable onPress={() => router.back()} hitSlop={8} className="h-9 w-9 items-center justify-center active:opacity-60">
            <Ionicons name="chevron-back" size={24} color="#1E2119" />
          </Pressable>
          <Text className="flex-1 text-center text-base font-extrabold text-ink">{title}</Text>
          <View className="h-9 w-9" />
        </View>

        <ScrollView contentContainerStyle={{ paddingHorizontal: 20, paddingBottom: 24 }} showsVerticalScrollIndicator={false}>
          {!isCompleted ? <MapPreview km={task.distanceKm} eta={task.etaMinutes} /> : null}

          {/* Customer card */}
          <View
            className="mt-4 flex-row items-center rounded-3xl bg-white p-4"
            style={{ shadowColor: '#000', shadowOpacity: 0.05, shadowRadius: 8, shadowOffset: { width: 0, height: 3 }, elevation: 2 }}
          >
            <Avatar name={task.customerName} size={44} textClassName="text-sm" />
            <View className="ml-3 flex-1">
              <Text className="text-base font-extrabold text-ink">{task.customerName}</Text>
              <Text className="text-xs text-ink-muted">{task.addressLine}</Text>
            </View>
            {!isCompleted ? (
              <View className="flex-row gap-2">
                <Pressable
                  onPress={() => void Linking.openURL(`tel:${task.customerPhone}`)}
                  className="h-10 w-10 items-center justify-center rounded-full border border-cream-300 active:opacity-70"
                  accessibilityLabel="Call customer"
                >
                  <Ionicons name="call-outline" size={18} color="#4A552A" />
                </Pressable>
                <Pressable
                  onPress={() => void Linking.openURL(`sms:${task.customerPhone}`)}
                  className="h-10 w-10 items-center justify-center rounded-full bg-olive-600 active:opacity-80"
                  accessibilityLabel="Message customer"
                >
                  <Ionicons name="chatbubble-ellipses-outline" size={18} color="#FFFFFF" />
                </Pressable>
              </View>
            ) : null}
          </View>

          {/* Delivery OTP */}
          {needsOtp ? (
            <View className="mt-4 rounded-3xl bg-gold-50 p-5">
              <Text className="text-center text-xs font-bold uppercase tracking-widest text-gold-700">
                Delivery OTP
              </Text>
              <View className="mt-3">
                <OtpInput value={code} onChangeText={(t) => { setOtpError(false); setCode(t); }} length={4} accent="gold" hasError={otpError} autoFocus />
              </View>
              <Text className="mt-3 text-center text-xs text-ink-muted">
                {otpError ? "That code didn't match. Ask the customer again." : 'Ask the customer for the 4-digit code to confirm delivery.'}
              </Text>
            </View>
          ) : null}

          {/* Garments / payment row */}
          <View className="mt-4 flex-row items-center rounded-2xl bg-white px-4 py-3" style={{ elevation: 1 }}>
            <MaterialCommunityIcons name="hanger" size={18} color="#4A552A" />
            <Text className="ml-2 text-sm font-semibold text-ink">{task.garmentCount} garments</Text>
            <View className="flex-1" />
            {task.isPaid ? (
              <View className="flex-row items-center gap-1">
                <Ionicons name="checkmark-circle" size={16} color="#4F8A4F" />
                <Text className="text-sm font-bold text-success">Paid</Text>
              </View>
            ) : (
              <Text className="text-sm font-bold text-danger">₹{task.amountDue} to collect</Text>
            )}
          </View>

          {isCompleted ? (
            <View className="mt-4 items-center rounded-3xl bg-olive-100 p-5">
              <Ionicons name="checkmark-done-circle" size={32} color="#4A552A" />
              <Text className="mt-2 text-base font-extrabold text-ink">
                {isDelivery ? 'Delivered' : 'Picked up'}
              </Text>
              <Text className="mt-1 text-sm text-ink-muted">Earned ₹{task.payout} on this task</Text>
            </View>
          ) : null}
        </ScrollView>

        {/* Confirm */}
        {!isCompleted ? (
          <View className="px-5 pb-3 pt-1">
            <Button
              title={isDelivery ? 'Confirm delivered' : 'Confirm pickup'}
              iconLeft="checkmark"
              variant="confirm"
              size="lg"
              fullWidth
              disabled={!canConfirm}
              onPress={confirm}
            />
          </View>
        ) : null}
      </SafeAreaView>
    </View>
  );
}
