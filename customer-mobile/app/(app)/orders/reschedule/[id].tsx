/**
 * Reschedule pickup screen — R3-BE-3.
 *
 * Accessible from:
 *   - PickupTracking screen (via "Reschedule pickup" button)
 *   - PickupCard on my-orders (via long-press or action button on reschedulable statuses)
 *
 * Route param `id` — the pickup request UUID.
 *
 * Flow:
 *   1. Customer picks a new date (next 7 days).
 *   2. Customer optionally picks a new slot (same live slot grid as booking/pickup.tsx).
 *   3. Confirm → POST /customer/pickup-requests/{id}/reschedule.
 *   4. On success: invalidate queries + show success, navigate back.
 *   5. On 4xx (non-reschedulable status): show friendly message.
 *
 * R3-MOB-1: KeyboardAvoidingView (no text input here, but wrapping is consistent).
 */
import React, { useMemo, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  Pressable,
  ScrollView,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import { useDeliverySlots, useReschedulePickup } from '@/hooks/useOrders';
import { hapticError, hapticImpact, hapticSuccess } from '@/lib/haptics';
import { localDateIso } from '@/lib/format';
import type { DeliverySlotDto } from '@/types/api';

// ── Helpers ───────────────────────────────────────────────────────────────────

interface DayOption {
  iso: string;
  weekday: string;
  day: string;
  month: string;
}

function nextDays(n: number): DayOption[] {
  const out: DayOption[] = [];
  const base = new Date();
  for (let i = 0; i < n; i++) {
    const d = new Date(base);
    d.setDate(base.getDate() + i);
    out.push({
      // LOCAL date parts — toISOString() is UTC and yields *yesterday* before 05:30 IST
      iso: localDateIso(d),
      weekday: d.toLocaleDateString('en-IN', { weekday: 'short' }).toUpperCase(),
      day: String(d.getDate()),
      month: d.toLocaleDateString('en-IN', { month: 'short' }),
    });
  }
  return out;
}

function formatSlotLabel(slot: DeliverySlotDto): string {
  const fmt = (t: string) => {
    const [h, m] = t.split(':');
    const hour = parseInt(h, 10);
    const min = m === '00' ? '' : `:${m}`;
    const period = hour < 12 ? 'AM' : 'PM';
    const h12 = hour % 12 === 0 ? 12 : hour % 12;
    return `${h12}${min} ${period}`;
  };
  return `${fmt(slot.slotStart)} – ${fmt(slot.slotEnd)}`;
}

// ── Screen ────────────────────────────────────────────────────────────────────

export default function RescheduleScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const { id } = useLocalSearchParams<{ id: string }>();
  const safeId = id ?? '';

  const days = useMemo(() => nextDays(7), []);
  const [selectedDay, setSelectedDay] = useState<DayOption>(days[0]);
  const [selectedSlotId, setSelectedSlotId] = useState<string | null>(null);

  const { data: slots, isLoading: slotsLoading } = useDeliverySlots(undefined, selectedDay.iso);
  const rescheduleMutation = useReschedulePickup(safeId);

  // Reset slot selection whenever the day changes.
  const handleDaySelect = (day: DayOption) => {
    hapticImpact();
    setSelectedDay(day);
    setSelectedSlotId(null);
  };

  const handleSlotSelect = (slot: DeliverySlotDto) => {
    if (!slot.available) return;
    hapticImpact();
    setSelectedSlotId(slot.id === selectedSlotId ? null : slot.id);
  };

  const handleConfirm = async () => {
    hapticImpact();
    try {
      await rescheduleMutation.mutateAsync({
        newDate: selectedDay.iso,
        newSlotId: selectedSlotId ?? null,
      });
      hapticSuccess();
      Alert.alert(
        t('reschedule.success'),
        t('reschedule.successMessage'),
        [{ text: t('common.ok'), onPress: () => router.back() }],
      );
    } catch (err: unknown) {
      hapticError();
      const msg = err instanceof Error ? err.message : '';
      // BusinessRuleException from backend has the "cannot be rescheduled" message embedded.
      const isStatusError =
        msg.toLowerCase().includes('cannot be rescheduled') ||
        msg.toLowerCase().includes('status');
      Alert.alert(
        t('reschedule.error'),
        isStatusError ? t('reschedule.errorNotAllowed') : t('reschedule.errorGeneric'),
      );
    }
  };

  const availableSlots = slots?.filter((s) => s.slotType === 'pickup') ?? [];

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
        <View className="flex-1">
          <Text className="text-xl font-extrabold text-ink">{t('reschedule.title')}</Text>
          <Text className="text-xs text-ink-muted">{t('reschedule.subtitle')}</Text>
        </View>
      </View>

      <ScrollView
        showsVerticalScrollIndicator={false}
        contentContainerStyle={{ paddingBottom: 140 }}
      >
        {/* Day picker */}
        <Text className="mx-5 mb-3 mt-2 text-[11px] font-bold uppercase tracking-wider text-ink-faint">
          {t('booking.pickADay')}
        </Text>
        <ScrollView
          horizontal
          showsHorizontalScrollIndicator={false}
          contentContainerStyle={{ paddingHorizontal: 20, gap: 8 }}
        >
          {days.map((d) => {
            const isSelected = selectedDay.iso === d.iso;
            return (
              <Pressable
                key={d.iso}
                onPress={() => handleDaySelect(d)}
                accessibilityRole="button"
                accessibilityState={{ selected: isSelected }}
                accessibilityLabel={`${d.weekday} ${d.day} ${d.month}`}
                className={[
                  'items-center rounded-2xl px-4 py-3 min-w-[58px]',
                  isSelected ? 'bg-olive-700' : 'bg-white',
                ].join(' ')}
              >
                <Text className={`text-[10px] font-bold ${isSelected ? 'text-olive-100' : 'text-ink-faint'}`}>
                  {d.weekday}
                </Text>
                <Text className={`text-xl font-extrabold ${isSelected ? 'text-white' : 'text-ink'}`}>
                  {d.day}
                </Text>
                <Text className={`text-[10px] ${isSelected ? 'text-olive-200' : 'text-ink-muted'}`}>
                  {d.month}
                </Text>
              </Pressable>
            );
          })}
        </ScrollView>

        {/* Slot picker */}
        <Text className="mx-5 mb-3 mt-6 text-[11px] font-bold uppercase tracking-wider text-ink-faint">
          {t('booking.pickASlot')}
        </Text>

        {slotsLoading ? (
          <View className="mx-5 items-center py-8">
            <ActivityIndicator color="#4A552A" />
          </View>
        ) : availableSlots.length === 0 ? (
          <View className="mx-5 rounded-2xl bg-white px-4 py-5">
            <Text className="text-center text-sm text-ink-muted">{t('booking.noSlotsForDay')}</Text>
          </View>
        ) : (
          <View className="mx-5 flex-row flex-wrap gap-3">
            {availableSlots.map((slot) => {
              const isSelected = slot.id === selectedSlotId;
              const isFull = !slot.available;
              const fewLeft = slot.available && (slot.capacity - slot.bookedCount) <= 2;
              return (
                <Pressable
                  key={slot.id}
                  onPress={() => handleSlotSelect(slot)}
                  disabled={isFull}
                  accessibilityRole="button"
                  accessibilityState={{ selected: isSelected, disabled: isFull }}
                  accessibilityLabel={formatSlotLabel(slot)}
                  style={{ width: '47%' }}
                  className={[
                    'rounded-2xl border p-4',
                    isFull
                      ? 'border-cream-200 bg-cream-100 opacity-50'
                      : isSelected
                        ? 'border-olive-600 bg-olive-50'
                        : 'border-cream-300 bg-white',
                  ].join(' ')}
                >
                  {slot.isExpress ? (
                    <View className="mb-1 self-start rounded-full bg-gold-200 px-2 py-0.5">
                      <Text className="text-[9px] font-bold text-gold-800">EXPRESS</Text>
                    </View>
                  ) : null}
                  <Text className={`text-sm font-bold ${isSelected ? 'text-olive-800' : 'text-ink'}`}>
                    {formatSlotLabel(slot)}
                  </Text>
                  <Text className={`mt-0.5 text-xs ${isFull ? 'text-danger' : fewLeft ? 'text-gold-700' : 'text-ink-muted'}`}>
                    {isFull ? t('booking.slotFull') : fewLeft ? t('booking.fewLeft') : t('booking.available')}
                  </Text>
                </Pressable>
              );
            })}
          </View>
        )}
      </ScrollView>

      {/* Confirm button */}
      <View
        className="absolute inset-x-0 bottom-0 border-t border-cream-200 bg-white px-6 pb-8 pt-4"
        style={{ borderTopLeftRadius: 28, borderTopRightRadius: 28 }}
      >
        <Pressable
          onPress={() => void handleConfirm()}
          disabled={rescheduleMutation.isPending}
          className={[
            'flex-row items-center justify-center gap-2 rounded-2xl py-4',
            rescheduleMutation.isPending ? 'bg-olive-400' : 'bg-olive-700',
          ].join(' ')}
          accessibilityRole="button"
          accessibilityLabel={t('reschedule.confirm')}
          accessibilityState={{ disabled: rescheduleMutation.isPending }}
        >
          {rescheduleMutation.isPending ? (
            <ActivityIndicator size="small" color="#FFFFFF" />
          ) : null}
          <Text className="text-base font-extrabold text-white">
            {rescheduleMutation.isPending ? t('reschedule.confirming') : t('reschedule.confirm')}
          </Text>
        </Pressable>
      </View>
    </SafeAreaView>
  );
}
