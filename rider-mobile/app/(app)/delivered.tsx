/**
 * Delivered! — task completion summary (mockup: success screen).
 *
 * Reads the just-completed task (?id=) plus the live stats to show the
 * customer rating, this-task earnings, running total and the next task.
 */
import React from 'react';
import { Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { StatusBar } from 'expo-status-bar';
import { useRiderTask, useRiderTasks } from '@/hooks/useRiderTasks';
import { Button } from '@/components/ui/Button';
import { useTranslation } from 'react-i18next';
import type { RiderTask } from '@/types/api';

function timeLabel(iso?: string): string {
  if (!iso) return '';
  try {
    return new Date(iso).toLocaleTimeString('en-IN', { hour: 'numeric', minute: '2-digit' });
  } catch {
    return '';
  }
}

function nextWhen(t: RiderTask): string {
  if (t.windowStart && t.windowEnd) return `${t.windowStart}–${t.windowEnd}`;
  if (t.scheduledTime) return t.scheduledTime;
  return 'Any time';
}

function Confetti() {
  const bits = [
    { left: '12%', top: 10, color: '#DBAC3D', rot: '20deg' },
    { left: '24%', top: 40, color: '#4A552A', rot: '-15deg' },
    { left: '40%', top: 8,  color: '#C0492F', rot: '40deg' },
    { left: '58%', top: 30, color: '#73803F', rot: '-25deg' },
    { left: '72%', top: 12, color: '#DBAC3D', rot: '12deg' },
    { left: '84%', top: 44, color: '#3F6E8C', rot: '-30deg' },
    { left: '66%', top: 60, color: '#C0492F', rot: '18deg' },
    { left: '30%', top: 64, color: '#DBAC3D', rot: '-10deg' },
  ];
  return (
    <View pointerEvents="none" style={{ position: 'absolute', left: 0, right: 0, top: 0, height: 110 }}>
      {bits.map((b, i) => (
        <View
          key={i}
          style={{ position: 'absolute', left: b.left as any, top: b.top, width: 8, height: 14, borderRadius: 2, backgroundColor: b.color, transform: [{ rotate: b.rot }] }}
        />
      ))}
    </View>
  );
}

export default function DeliveredScreen() {
  const router = useRouter();
  const { t } = useTranslation();
  const { id } = useLocalSearchParams<{ id: string }>();
  const { task } = useRiderTask(id);
  const { pending, stats } = useRiderTasks();

  const isDelivery = task?.legType !== 'pickup';
  const next = pending[0];

  return (
    <SafeAreaView className="flex-1 bg-cream">
      <StatusBar style="dark" />
      <View className="flex-1 px-6">
        {/* Hero */}
        <View className="items-center pt-12">
          <Confetti />
          <View className="h-20 w-20 items-center justify-center rounded-full bg-olive-600" style={{ shadowColor: '#3B4423', shadowOpacity: 0.3, shadowRadius: 16, shadowOffset: { width: 0, height: 8 }, elevation: 6 }}>
            <Ionicons name="checkmark" size={40} color="#FFFFFF" />
          </View>
          <Text className="mt-5 text-3xl font-extrabold text-ink">
            {isDelivery ? t('taskDetail.delivery') + '!' : t('taskDetail.pickup') + '!'}
          </Text>
          {task ? (
            <Text className="mt-2 text-center text-sm text-ink-muted">
              {task.customerName} {isDelivery ? 'received' : '· collected'} #{task.orderNumber}
              {task.completedAt ? <Text className="font-bold text-ink-soft">{`  ·  ${timeLabel(task.completedAt)}`}</Text> : null}
            </Text>
          ) : null}
        </View>

        {/* Rating */}
        {task?.rating ? (
          <View className="mt-8 flex-row items-center rounded-2xl bg-gold-50 p-4">
            <Ionicons name="star" size={20} color="#DBAC3D" />
            <View className="ml-3 flex-1">
              <Text className="text-sm font-bold text-ink">Customer rated you {task.rating}/5</Text>
              <Text className="text-xs italic text-ink-muted">“Friendly and on time, as always!”</Text>
            </View>
          </View>
        ) : null}

        {/* Earnings */}
        <View className="mt-4 rounded-2xl bg-white p-5" style={{ elevation: 2, shadowColor: '#000', shadowOpacity: 0.05, shadowRadius: 8, shadowOffset: { width: 0, height: 3 } }}>
          <View className="flex-row items-center justify-between border-b border-cream-200 py-2.5">
            <Text className="text-sm text-ink-muted">Earnings (this task)</Text>
            <Text className="text-sm font-extrabold text-olive-700">+₹{task?.payout ?? 0}</Text>
          </View>
          <View className="flex-row items-center justify-between border-b border-cream-200 py-2.5">
            <Text className="text-sm text-ink-muted">Total today</Text>
            <Text className="text-sm font-extrabold text-ink">₹{stats.earnedToday}</Text>
          </View>
          <View className="flex-row items-center justify-between py-2.5">
            <Text className="text-sm text-ink-muted">Next task</Text>
            <Text className="text-sm font-semibold text-ink">
              {next ? `${nextWhen(next)} · ${next.zoneLabel ?? ''}`.trim() : 'No more tasks'}
            </Text>
          </View>
        </View>

        <View className="flex-1" />

        <View className="pb-4">
          <Button
            title={pending.length > 0 ? `${t('tasks.title')} (${pending.length})` : t('tasks.title')}
            size="lg"
            fullWidth
            onPress={() => router.replace('/(app)/tasks')}
          />
        </View>
      </View>
    </SafeAreaView>
  );
}
