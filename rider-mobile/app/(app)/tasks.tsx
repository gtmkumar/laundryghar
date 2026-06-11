/**
 * Today's tasks — pickup/delivery job queue (mockup: tasks list).
 *
 * Olive header with the rider's zone + live stats (completed/total, earnings,
 * rating), Tasks/Done content tabs, and the job cards. The imminent (first
 * pending) task is expanded with Call + Start actions; the rest are compact.
 *
 * Data: useRiderTasks (real /rider/tasks when the backend ships it, demo set
 * otherwise). Tapping a card opens the delivery/pickup detail.
 */
import React, { useState } from 'react';
import { FlatList, Linking, Pressable, RefreshControl, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons, MaterialCommunityIcons } from '@expo/vector-icons';
import { StatusBar } from 'expo-status-bar';
import { useRiderTasks } from '@/hooks/useRiderTasks';
import { useMyRiderProfile } from '@/hooks/useRider';
import { useAuthStore } from '@/store/authStore';
import { useDutyStore } from '@/store/dutyStore';
import { TasksListSkeleton } from '@/components/ui/Skeleton';
import { Button } from '@/components/ui/Button';
import { useTranslation } from 'react-i18next';
import type { RiderTask } from '@/types/api';

function whenLabel(t: RiderTask): string {
  if (t.windowStart && t.windowEnd) return `${t.windowStart}–${t.windowEnd}`;
  if (t.scheduledTime) return t.scheduledTime;
  return '';
}

function LegIcon({ legType, size = 16 }: { legType: RiderTask['legType']; size?: number }) {
  return (
    <MaterialCommunityIcons
      name={legType === 'delivery' ? 'arrow-down' : 'arrow-up'}
      size={size}
      color="#8A641D"
    />
  );
}

/** Right-aligned status badges. OTP applies to both legs; payment/express shown alongside. */
function Tags({ task }: { task: RiderTask }) {
  const { t } = useTranslation();
  const hasOtp = task.requiresOtp ?? (task.legType !== 'pickup' && !!task.deliveryOtp);
  const cod    = !task.isPaid && task.amountDue > 0;
  return (
    <View className="shrink-0 flex-row items-center gap-1.5">
      {hasOtp ? (
        <View className="flex-row items-center gap-1 rounded-lg bg-olive-100 px-2 py-1">
          <Ionicons name="key-outline" size={12} color="#4A552A" />
          <Text className="text-[11px] font-bold text-olive-800">{t('tasks.otp')}</Text>
        </View>
      ) : null}
      {cod ? (
        <View className="rounded-lg bg-danger/10 px-2 py-1">
          <Text className="text-[11px] font-bold text-danger">{t('tasks.collect', { amount: task.amountDue })}</Text>
        </View>
      ) : task.isExpress ? (
        <View className="rounded-lg bg-gold-100 px-2 py-1">
          <Text className="text-[11px] font-bold text-gold-700">{t('tasks.express')}</Text>
        </View>
      ) : null}
    </View>
  );
}

function CardShell({ children, onPress }: { children: React.ReactNode; onPress: () => void }) {
  return (
    <Pressable
      onPress={onPress}
      className="mb-3 rounded-3xl bg-white p-4 active:opacity-80"
      style={{ shadowColor: '#000', shadowOpacity: 0.05, shadowRadius: 8, shadowOffset: { width: 0, height: 3 }, elevation: 2 }}
    >
      {children}
    </Pressable>
  );
}

function CardHeader({ task }: { task: RiderTask }) {
  const { t } = useTranslation();
  return (
    <View className="mb-2 flex-row items-center gap-2">
      <View className="h-6 w-6 items-center justify-center rounded-lg bg-gold-100">
        <LegIcon legType={task.legType} />
      </View>
      <Text className="text-sm font-bold text-ink-soft">
        {task.legType === 'pickup' ? t('taskDetail.pickup') : t('taskDetail.delivery')}
      </Text>
      {/* flex-1 + middle ellipsis so long order numbers never collide with the badges */}
      <Text
        numberOfLines={1}
        ellipsizeMode="middle"
        className="flex-1 text-sm font-semibold text-ink-faint"
      >
        #{task.orderNumber}
      </Text>
      <Tags task={task} />
    </View>
  );
}

function MetaRow({ task }: { task: RiderTask }) {
  return (
    <View className="mt-2 flex-row items-center">
      <Ionicons name="navigate-outline" size={13} color="#7B7A6C" />
      <Text className="ml-1 text-xs font-semibold text-ink-muted">{task.distanceKm} km</Text>
      {whenLabel(task) ? (
        <>
          <View className="mx-2 h-1 w-1 rounded-full bg-ink-faint" />
          <Ionicons name="time-outline" size={13} color="#7B7A6C" />
          <Text className="ml-1 text-xs font-semibold text-ink-muted">{whenLabel(task)}</Text>
        </>
      ) : null}
      <View className="flex-1" />
      <Text className="text-xs font-bold text-olive-700">₹{task.payout}</Text>
    </View>
  );
}

function PendingCard({ task, expanded, onOpen }: { task: RiderTask; expanded: boolean; onOpen: () => void }) {
  const { t } = useTranslation();
  return (
    <CardShell onPress={onOpen}>
      <CardHeader task={task} />
      <Text className="text-base font-extrabold text-ink">{task.customerName}</Text>
      <View className="mt-0.5 flex-row items-center">
        <Ionicons name="location-outline" size={13} color="#A8A493" />
        <Text className="ml-1 text-xs text-ink-muted">{task.addressLine}</Text>
      </View>
      <MetaRow task={task} />

      {expanded ? (
        <View className="mt-3 flex-row gap-3">
          <Button
            title={t('tasks.call')}
            iconLeft="call-outline"
            variant="secondary"
            size="sm"
            onPress={() => void Linking.openURL(`tel:${task.customerPhone}`)}
          />
          <View className="flex-1">
            <Button title={t('tasks.start')} iconRight="arrow-forward" size="sm" fullWidth onPress={onOpen} />
          </View>
        </View>
      ) : null}
    </CardShell>
  );
}

function DoneCard({ task, onOpen }: { task: RiderTask; onOpen: () => void }) {
  const { t } = useTranslation();
  return (
    <CardShell onPress={onOpen}>
      <View className="flex-row items-center">
        <View className="h-9 w-9 items-center justify-center rounded-full bg-olive-100">
          <Ionicons name="checkmark" size={18} color="#4A552A" />
        </View>
        <View className="ml-3 flex-1">
          <Text className="text-sm font-bold text-ink">
            {task.legType === 'delivery' ? t('taskDetail.delivery') : t('taskDetail.pickup')} · {task.customerName}
          </Text>
          <Text className="text-xs text-ink-muted">#{task.orderNumber} · {task.addressLine}</Text>
        </View>
        <View className="items-end">
          <Text className="text-sm font-bold text-olive-700">+₹{task.payout}</Text>
          {task.rating ? (
            <View className="flex-row items-center">
              <Ionicons name="star" size={11} color="#DBAC3D" />
              <Text className="ml-0.5 text-xs text-ink-muted">{task.rating}</Text>
            </View>
          ) : null}
        </View>
      </View>
    </CardShell>
  );
}

function StatTile({ label, value, sub }: { label: string; value: string; sub?: React.ReactNode }) {
  return (
    <View className="flex-1 items-center">
      <View className="flex-row items-center">
        <Text className="text-xl font-extrabold text-white">{value}</Text>
        {sub}
      </View>
      <Text className="mt-0.5 text-[11px] uppercase tracking-wider text-olive-100">{label}</Text>
    </View>
  );
}

export default function TasksScreen() {
  const router = useRouter();
  const { t } = useTranslation();
  const { rider } = useAuthStore();
  const { isOnDuty } = useDutyStore();
  const { data: me } = useMyRiderProfile();
  const { pending, done, stats, isDemo, isLoading, refetch, isRefetching } = useRiderTasks();
  const [tab, setTab] = useState<'tasks' | 'done'>('tasks');

  const profile = me ?? rider;
  const name = profile?.riderName?.trim() || profile?.riderCode || 'Rider';
  const rating = profile?.ratingAverage != null ? profile.ratingAverage.toFixed(1) : '—';

  if (isLoading) return <TasksListSkeleton />;

  const data = tab === 'tasks' ? pending : done;

  return (
    <View className="flex-1 bg-cream">
      <StatusBar style="light" />
      {/* Olive header */}
      <SafeAreaView edges={['top']} style={{ backgroundColor: '#4A552A' }}>
        <View className="px-5 pb-5 pt-2">
          <View className="flex-row items-center justify-between">
            <View className="flex-row items-center">
              <Pressable onPress={() => router.back()} hitSlop={8} accessibilityRole="button" accessibilityLabel={t('a11y.back')} className="mr-1 active:opacity-60">
                <Ionicons name="chevron-back" size={22} color="#FFFFFF" />
              </Pressable>
              <Text className="text-xs font-bold uppercase tracking-widest text-olive-100">
                Rider · {stats.zoneLabel} zone
              </Text>
            </View>
            <View className="flex-row items-center gap-1.5 rounded-full bg-white/15 px-3 py-1">
              <View className={`h-2 w-2 rounded-full ${isOnDuty ? 'bg-gold-300' : 'bg-ink-faint'}`} />
              <Text className="text-xs font-bold text-white">{isOnDuty ? t('home.online') : t('home.offline')}</Text>
            </View>
          </View>

          <Text className="mt-2 text-2xl font-extrabold text-white">{name}</Text>

          <View className="mt-4 flex-row">
            <StatTile label="Today" value={`${stats.completed}/${stats.total}`} />
            <View className="w-px bg-white/15" />
            <StatTile label="Earned" value={`₹${stats.earnedToday}`} />
            <View className="w-px bg-white/15" />
            <StatTile
              label="Rating"
              value={rating}
              sub={<Ionicons name="star" size={14} color="#DBAC3D" style={{ marginLeft: 3 }} />}
            />
          </View>
        </View>
      </SafeAreaView>

      {/* Tabs */}
      <View className="flex-row gap-2 px-5 pb-1 pt-4">
        {(['tasks', 'done'] as const).map((tabKey) => {
          const active = tab === tabKey;
          const count = tabKey === 'tasks' ? pending.length : done.length;
          return (
            <Pressable
              key={tabKey}
              onPress={() => setTab(tabKey)}
              accessibilityRole="tab"
              accessibilityState={{ selected: active }}
              accessibilityLabel={tabKey === 'tasks' ? t('a11y.tabTasks') : t('a11y.tabDone')}
              className={`rounded-full px-4 py-2 ${active ? 'bg-olive-700' : 'bg-white border border-cream-300'}`}
            >
              <Text className={`text-sm font-bold ${active ? 'text-white' : 'text-ink-muted'}`}>
                {tabKey === 'tasks' ? t('tasks.tab_tasks') : t('tasks.tab_done')} ({count})
              </Text>
            </Pressable>
          );
        })}
        <View className="flex-1" />
      </View>

      <FlatList
        data={data}
        keyExtractor={(t) => t.id}
        contentContainerStyle={{ paddingHorizontal: 20, paddingTop: 12, paddingBottom: 32 }}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl refreshing={isRefetching} onRefresh={() => void refetch()} tintColor="#4A552A" colors={['#4A552A']} />
        }
        ListHeaderComponent={
          isDemo ? (
            <Text className="mb-3 text-center text-[11px] text-ink-faint">
              {t('home.demoNote')}
            </Text>
          ) : null
        }
        renderItem={({ item, index }) =>
          tab === 'tasks' ? (
            <PendingCard
              task={item}
              expanded={index === 0}
              onOpen={() => router.push(`/(app)/tasks/${item.id}`)}
            />
          ) : (
            <DoneCard task={item} onOpen={() => router.push(`/(app)/tasks/${item.id}`)} />
          )
        }
        ListEmptyComponent={
          <View className="items-center px-8 pt-16">
            <View className="h-16 w-16 items-center justify-center rounded-full bg-olive-100">
              <Ionicons name={tab === 'tasks' ? 'checkmark-done' : 'time-outline'} size={28} color="#4A552A" />
            </View>
            <Text className="mt-4 text-base font-bold text-ink">
              {tab === 'tasks' ? t('tasks.noTasks') : t('tasks.noDone')}
            </Text>
            <Text className="mt-1 text-center text-sm text-ink-muted">
              {tab === 'tasks' ? t('tasks.noTasksMessage') : t('tasks.noDoneMessage')}
            </Text>
          </View>
        }
      />
    </View>
  );
}
