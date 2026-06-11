/**
 * Earnings screen — rolling 7-day / 30-day earnings breakdown.
 *
 * Header: period total + average payout per task.
 * Body: per-day list (date, task count, payout). Each DayRow is tappable — it
 * expands in-line to show the individual tasks behind that day's total, fetched
 * by date-filtering the cached today+history task query.
 *
 * MOB-15: date-filtered tasks are resolved client-side from the React Query
 * cache (today's tasks via useRiderTasks; historical tasks would require a
 * date-range endpoint — noted for backend follow-up).
 */
import React, { useMemo, useState } from 'react';
import { FlatList, Pressable, RefreshControl, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons, MaterialCommunityIcons } from '@expo/vector-icons';
import { StatusBar } from 'expo-status-bar';
import { useMyPayouts } from '@/hooks/useEarnings';
import { useRiderTasks } from '@/hooks/useRiderTasks';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { useTranslation } from 'react-i18next';
import type { RiderPayoutDayDto, RiderTask } from '@/types/api';

function formatDate(iso: string): string {
  const d = new Date(iso + 'T00:00:00');
  return d.toLocaleDateString('en-IN', { weekday: 'short', day: 'numeric', month: 'short' });
}

/**
 * Return today's date as "YYYY-MM-DD" in the device's local timezone.
 * Using toISOString() would give UTC, which mis-buckets late-evening IST
 * completions (IST = UTC+5:30, so 23:30 IST → next UTC date).
 */
function todayIso(): string {
  const d = new Date();
  const year  = d.getFullYear();
  const month = String(d.getMonth() + 1).padStart(2, '0');
  const day   = String(d.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

function TaskLine({ task, onPress }: { task: RiderTask; onPress: () => void }) {
  return (
    <Pressable
      onPress={onPress}
      className="flex-row items-center rounded-2xl bg-cream-100 px-3 py-2.5 mb-2 active:opacity-70"
      accessibilityRole="button"
      accessibilityLabel={`Task ${task.orderNumber}, ₹${task.payout}`}
    >
      <View className="h-7 w-7 items-center justify-center rounded-full bg-olive-100">
        <MaterialCommunityIcons
          name={task.legType === 'delivery' ? 'arrow-down' : 'arrow-up'}
          size={14}
          color="#4A552A"
        />
      </View>
      <Text className="ml-2 flex-1 text-xs font-semibold text-ink">
        #{task.orderNumber} · {task.customerName}
      </Text>
      <Text className="text-xs font-bold text-olive-700">+₹{task.payout}</Text>
      <Ionicons name="chevron-forward" size={13} color="#A8A493" style={{ marginLeft: 4 }} />
    </Pressable>
  );
}

function DayRow({
  item,
  tasks,
  expanded,
  onToggle,
  onTaskPress,
}: {
  item: RiderPayoutDayDto;
  tasks: RiderTask[];
  expanded: boolean;
  onToggle: () => void;
  onTaskPress: (id: string) => void;
}) {
  const { t } = useTranslation();
  const isToday = item.date === todayIso();

  return (
    <View
      className={`mb-3 rounded-2xl bg-white ${expanded ? 'border border-olive-200' : ''}`}
      style={{ shadowColor: '#000', shadowOpacity: 0.04, shadowRadius: 6, shadowOffset: { width: 0, height: 2 }, elevation: 1 }}
    >
      <Pressable
        onPress={onToggle}
        className="flex-row items-center px-4 py-3.5 active:opacity-80"
        accessibilityRole="button"
        accessibilityLabel={`${formatDate(item.date)}, ${item.taskCount} tasks, ₹${item.totalPayout.toFixed(2)}`}
        accessibilityState={{ expanded }}
      >
        <View className="flex-1">
          <View className="flex-row items-center gap-1.5">
            <Text className="text-sm font-bold text-ink">{formatDate(item.date)}</Text>
            {isToday ? (
              <View className="rounded-full bg-olive-600 px-2 py-0.5">
                <Text className="text-[10px] font-bold text-white">Today</Text>
              </View>
            ) : null}
          </View>
          <Text className="mt-0.5 text-xs text-ink-muted">
            {t('earnings.taskCount', { count: item.taskCount })}
          </Text>
        </View>
        <Text className="mr-2 text-base font-extrabold text-olive-700">
          {item.totalPayout > 0 ? `+₹${item.totalPayout.toFixed(2)}` : '₹0'}
        </Text>
        <Ionicons
          name={expanded ? 'chevron-up' : 'chevron-down'}
          size={16}
          color="#A8A493"
        />
      </Pressable>

      {expanded ? (
        <View className="px-4 pb-4">
          <View className="mb-2 h-px bg-cream-200" />
          {tasks.length > 0 ? (
            tasks.map((task) => (
              <TaskLine
                key={task.id}
                task={task}
                onPress={() => onTaskPress(task.id)}
              />
            ))
          ) : (
            <View className="py-3">
              <Text className="text-center text-xs text-ink-muted">
                {isToday
                  ? 'No completed tasks yet today.'
                  : 'Task detail not available for past dates — backend date-range endpoint pending (MOB-15).'}
              </Text>
            </View>
          )}
        </View>
      ) : null}
    </View>
  );
}

export default function EarningsScreen() {
  const router = useRouter();
  const { t } = useTranslation();
  const [days, setDays] = useState<7 | 30>(30);
  const [expandedDate, setExpandedDate] = useState<string | null>(null);

  const { data, isLoading, isError, refetch, isRefetching } = useMyPayouts(days);
  // Tasks available in the client cache (today's set)
  const { tasks: cachedTasks } = useRiderTasks();

  const today = todayIso();

  /**
   * For today we can drill into the cached task set. For past dates we do not
   * yet have a date-range query — the row expands with an explanatory note.
   * When the backend ships GET /rider/tasks?date=YYYY-MM-DD this should call
   * that endpoint via a new query.
   */
  const tasksForDate = useMemo<Map<string, RiderTask[]>>(() => {
    const map = new Map<string, RiderTask[]>();
    // Only map today's completed tasks — historical data requires a backend endpoint.
    const todayDone = cachedTasks.filter(
      (t) => t.status === 'completed' && t.completedAt?.startsWith(today),
    );
    if (todayDone.length > 0) map.set(today, todayDone);
    return map;
  }, [cachedTasks, today]);

  if (isLoading) return <ScreenLoader />;
  if (isError) return <ErrorState message="Could not load earnings. Pull to refresh." />;

  const totalPayout = data?.totalPayout ?? 0;
  const avgPerTask  = data?.avgPerTask  ?? 0;
  const breakdown   = data?.breakdown   ?? [];

  return (
    <View className="flex-1 bg-cream">
      <StatusBar style="light" />

      {/* Olive header */}
      <SafeAreaView edges={['top']} style={{ backgroundColor: '#4A552A' }}>
        <View className="px-5 pb-6 pt-2">
          <View className="flex-row items-center">
            <Pressable
              onPress={() => router.back()}
              hitSlop={8}
              className="mr-2 h-9 w-9 items-center justify-center active:opacity-60"
              accessibilityLabel={t('a11y.back')}
              accessibilityRole="button"
            >
              <Ionicons name="chevron-back" size={24} color="#FFFFFF" />
            </Pressable>
            <Text className="flex-1 text-base font-extrabold text-white">{t('earnings.title')}</Text>
          </View>

          {/* Summary card */}
          <View className="mt-5 flex-row">
            <View className="flex-1">
              <Text className="text-xs uppercase tracking-widest text-olive-200">
                {days === 7 ? t('earnings.last7Days') : t('earnings.last30Days')}
              </Text>
              <Text className="mt-1 text-3xl font-extrabold text-white">
                ₹{totalPayout.toFixed(2)}
              </Text>
            </View>
            <View className="items-end justify-center">
              <Text className="text-xs text-olive-200">{t('earnings.avgPerTask')}</Text>
              <Text className="mt-0.5 text-xl font-extrabold text-gold-300">
                ₹{avgPerTask.toFixed(2)}
              </Text>
            </View>
          </View>

          {/* Period toggle */}
          <View className="mt-4 flex-row gap-2 self-start">
            {([7, 30] as const).map((d) => (
              <Pressable
                key={d}
                onPress={() => { setDays(d); setExpandedDate(null); }}
                accessibilityRole="button"
                accessibilityLabel={`Show last ${d} days`}
                accessibilityState={{ selected: days === d }}
                className={`rounded-full px-4 py-1.5 ${days === d ? 'bg-white' : 'bg-white/20'}`}
              >
                <Text className={`text-sm font-bold ${days === d ? 'text-olive-800' : 'text-white'}`}>
                  {d}d
                </Text>
              </Pressable>
            ))}
          </View>
        </View>
      </SafeAreaView>

      <FlatList
        data={breakdown}
        keyExtractor={(item) => item.date}
        contentContainerStyle={{ paddingHorizontal: 20, paddingTop: 16, paddingBottom: 40 }}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl
            refreshing={isRefetching}
            onRefresh={() => void refetch()}
            tintColor="#4A552A"
            colors={['#4A552A']}
          />
        }
        renderItem={({ item }) => (
          <DayRow
            item={item}
            tasks={tasksForDate.get(item.date) ?? []}
            expanded={expandedDate === item.date}
            onToggle={() => setExpandedDate((prev) => (prev === item.date ? null : item.date))}
            onTaskPress={(id) => router.push(`/(app)/tasks/${id}` as never)}
          />
        )}
        ListEmptyComponent={
          <View className="items-center px-8 pt-16">
            <View className="h-16 w-16 items-center justify-center rounded-full bg-olive-100">
              <Ionicons name="cash-outline" size={28} color="#4A552A" />
            </View>
            <Text className="mt-4 text-base font-bold text-ink">{t('earnings.noEarnings')}</Text>
            <Text className="mt-1 text-center text-sm text-ink-muted">
              {t('earnings.noEarningsMessage')}
            </Text>
          </View>
        }
      />
    </View>
  );
}
