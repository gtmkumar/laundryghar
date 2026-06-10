/**
 * Duty home — "Go on duty" (mockups: offline + online states).
 *
 * Going on/off duty calls setOnDuty() in dutyStore, which:
 *   1. Flips local AsyncStorage state immediately (optimistic — UX never waits).
 *   2. Calls PATCH /api/v1/rider/duty best-effort to persist the state server-side
 *      (auto-dispatch and the admin live board rely on is_on_duty=true).
 *
 * In addition, going on duty fires two more best-effort side effects here:
 *   3. Activates today's scheduled shift assignment (PATCH …/status → active).
 *   4. Sends a location ping so dispatch can see the rider on the map immediately.
 *
 * The "View today's tasks" CTA unlocks only once on duty (matches the mockup,
 * where it's faded while offline).
 */
import React, { useEffect, useState } from 'react';
import { Alert, Pressable, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons, MaterialCommunityIcons } from '@expo/vector-icons';
import { StatusBar } from 'expo-status-bar';
import { LinearGradient } from 'expo-linear-gradient';
import { useMyRiderProfile, useTodaysAssignments, useUpdateAssignmentStatus } from '@/hooks/useRider';
import { useRiderTasks } from '@/hooks/useRiderTasks';
import { useAuthStore } from '@/store/authStore';
import { useDutyStore, type ChecklistItem } from '@/store/dutyStore';
import { sendCurrentLocationPing } from '@/lib/sendCurrentLocation';
import { Avatar } from '@/components/ui/Avatar';
import { Button } from '@/components/ui/Button';
import { useTranslation } from 'react-i18next';

function greeting(): string {
  const h = new Date().getHours();
  if (h < 12) return 'Good morning';
  if (h < 17) return 'Good afternoon';
  return 'Good evening';
}

export default function HomeScreen() {
  const router = useRouter();
  const { t } = useTranslation();
  const { rider, setRider } = useAuthStore();
  const { isOnDuty, setOnDuty, checklist, toggleChecklist } = useDutyStore();
  const [busy, setBusy] = useState(false);

  const CHECKLIST: { key: ChecklistItem; label: string }[] = [
    { key: 'bagTags',      label: t('home.checklist.bagTags') },
    { key: 'phoneCharged', label: t('home.checklist.phoneCharged') },
    { key: 'vehicleDocs',  label: t('home.checklist.vehicleDocs') },
    { key: 'cashFloat',    label: t('home.checklist.cashFloat') },
  ];

  const { data: me } = useMyRiderProfile();
  const { data: assignments } = useTodaysAssignments();
  const { mutateAsync: updateAssignment } = useUpdateAssignmentStatus();
  const { stats, isDemo } = useRiderTasks();

  useEffect(() => { if (me) setRider(me); }, [me, setRider]);

  const profile = me ?? rider;
  const name = profile?.riderName?.trim() || profile?.riderCode || 'Rider';
  const zone =
    stats.zoneLabel !== 'your zone'
      ? stats.zoneLabel
      : profile?.primaryStoreName || profile?.franchiseName || 'your zone';

  async function goOnDuty() {
    setBusy(true);
    setOnDuty(true); // optimistic — UI flips immediately
    try {
      // (1) activate today's scheduled assignment, if any
      const scheduled = assignments?.find((a) => a.status === 'scheduled');
      const active    = assignments?.find((a) => a.status === 'active');
      if (scheduled) {
        await updateAssignment({ id: scheduled.id, status: 'active' }).catch(() => undefined);
      }
      // (2) send a location ping
      const outcome = await sendCurrentLocationPing(active?.id ?? scheduled?.id ?? null);
      if (!outcome.ok && outcome.reason === 'permission') {
        Alert.alert(
          'Location off',
          'You are on duty, but location permission is needed so dispatch can route tasks to you. Enable it in Settings.',
        );
      }
    } finally {
      setBusy(false);
    }
  }

  async function goOffDuty() {
    setOnDuty(false);
    const active = assignments?.find((a) => a.status === 'active');
    if (active) {
      await updateAssignment({ id: active.id, status: 'on_break' }).catch(() => undefined);
    }
  }

  function toggleDuty() {
    if (isOnDuty) {
      Alert.alert(t('home.goOffDuty'), t('home.goOffDutyMessage'), [
        { text: t('home.stayOnline'), style: 'cancel' },
        { text: t('home.goOnDuty'), style: 'destructive', onPress: () => void goOffDuty() },
      ]);
    } else {
      void goOnDuty();
    }
  }

  return (
    <View className="flex-1 bg-cream">
      <StatusBar style="dark" />
      {/* online tint behind the header */}
      {isOnDuty ? (
        <LinearGradient
          colors={['#E3E7D0', '#F3EEE3']}
          style={{ position: 'absolute', top: 0, left: 0, right: 0, height: 320 }}
        />
      ) : null}

      <SafeAreaView className="flex-1" edges={['top', 'left', 'right']}>
        <ScrollView contentContainerStyle={{ paddingBottom: 24 }} showsVerticalScrollIndicator={false}>
          {/* Header */}
          <View className="flex-row items-center px-5 pt-2">
            <Pressable onPress={() => router.push('/(app)/profile')} accessibilityRole="button" accessibilityLabel={t('a11y.viewProfile')} className="flex-row items-center active:opacity-70">
              <Avatar name={name} size={44} />
              <View className="ml-3">
                <Text className="text-xs text-ink-muted">{greeting()}</Text>
                <Text className="text-lg font-extrabold text-ink">{name}</Text>
              </View>
            </Pressable>
            <View className="flex-1" />
            <Pressable
              onPress={() => Alert.alert(t('home.notifications'), t('home.allCaughtUp'))}
              className="h-11 w-11 items-center justify-center rounded-2xl border border-cream-300 bg-white active:opacity-70"
              accessibilityRole="button"
              accessibilityLabel={t('a11y.notifications')}
            >
              <Ionicons name="notifications-outline" size={22} color="#3C3F35" />
            </Pressable>
          </View>

          {/* Duty circle */}
          <View className="items-center py-10">
            <Pressable
              onPress={toggleDuty}
              disabled={busy}
              accessibilityRole="switch"
              accessibilityLabel={t(isOnDuty ? 'a11y.goOffDuty' : 'a11y.goOnDuty')}
              accessibilityState={{ checked: isOnDuty, busy, disabled: busy }}
              className="items-center justify-center rounded-full active:opacity-90"
              style={{
                width: 200,
                height: 200,
                backgroundColor: isOnDuty ? '#5C6A33' : '#FFFFFF',
                shadowColor: '#3B4423',
                shadowOpacity: isOnDuty ? 0.3 : 0.12,
                shadowRadius: 24,
                shadowOffset: { width: 0, height: 12 },
                elevation: 6,
              }}
            >
              <MaterialCommunityIcons
                name="truck-fast-outline"
                size={48}
                color={isOnDuty ? '#FFFFFF' : '#3C3F35'}
              />
              <Text
                className={`mt-3 text-2xl font-extrabold ${isOnDuty ? 'text-white' : 'text-ink'}`}
              >
                {isOnDuty ? t('home.online') : t('home.goOnDuty')}
              </Text>
            </Pressable>
            <Text className="mt-4 text-sm text-ink-muted">
              {isOnDuty ? t('home.receivingTasks') : t('home.tapToStart')}
            </Text>
          </View>

          {/* Before you ride checklist */}
          <View
            className="mx-5 rounded-3xl bg-white p-5"
            style={{ shadowColor: '#000', shadowOpacity: 0.05, shadowRadius: 10, shadowOffset: { width: 0, height: 4 }, elevation: 2 }}
          >
            <Text className="mb-3 text-xs font-bold uppercase tracking-widest text-ink-faint">
              {t('home.beforeYouRide')}
            </Text>
            {CHECKLIST.map((item, i) => {
              const checked = checklist[item.key];
              return (
                <Pressable
                  key={item.key}
                  onPress={() => toggleChecklist(item.key)}
                  className={`flex-row items-center py-2.5 active:opacity-70 ${i < CHECKLIST.length - 1 ? 'border-b border-cream-200' : ''}`}
                  accessibilityRole="checkbox"
                  accessibilityState={{ checked }}
                  accessibilityLabel={item.label}
                >
                  <View
                    className={`h-6 w-6 items-center justify-center rounded-full ${checked ? 'bg-olive-600' : 'border-2 border-cream-300 bg-cream-100'}`}
                  >
                    {checked ? <Ionicons name="checkmark" size={15} color="#FFFFFF" /> : null}
                  </View>
                  <Text className={`ml-3 text-base ${checked ? 'text-ink' : 'text-ink-muted'}`}>
                    {item.label}
                  </Text>
                </Pressable>
              );
            })}
          </View>

          {/* Tasks-waiting pill */}
          <View className="mx-5 mt-4 flex-row items-center rounded-2xl bg-cream-200 px-4 py-4">
            <View className="h-8 w-8 items-center justify-center rounded-lg bg-white">
              <Ionicons name="clipboard-outline" size={16} color="#5C6A33" />
            </View>
            <Text className="ml-3 flex-1 text-sm leading-5 text-ink-soft">
              {t('home.pendingTasks', { count: stats.pendingCount, zone })}{'\n'}
              {t('home.avgPayout', { amount: stats.avgPayout })}
            </Text>
          </View>

          {isDemo ? (
            <Text className="mx-5 mt-3 text-center text-[11px] text-ink-faint">
              {t('home.demoNote')}
            </Text>
          ) : null}

          {/* Quick-action row — Earnings + Cash */}
          <View className="mx-5 mt-4 flex-row gap-3">
            <Pressable
              onPress={() => router.push('/(app)/earnings')}
              className="flex-1 flex-row items-center gap-2 rounded-2xl bg-white px-4 py-3.5 active:opacity-70"
              style={{ shadowColor: '#000', shadowOpacity: 0.05, shadowRadius: 6, shadowOffset: { width: 0, height: 2 }, elevation: 1 }}
              accessibilityRole="button"
              accessibilityLabel={t('a11y.earnings')}
            >
              <View className="h-8 w-8 items-center justify-center rounded-lg bg-olive-100">
                <Ionicons name="trending-up-outline" size={16} color="#4A552A" />
              </View>
              <Text className="text-sm font-bold text-ink">{t('home.earnings')}</Text>
              <View className="flex-1" /><Ionicons name="chevron-forward" size={14} color="#A8A493" />
            </Pressable>

            <Pressable
              onPress={() => router.push('/(app)/cash')}
              className="flex-1 flex-row items-center gap-2 rounded-2xl bg-white px-4 py-3.5 active:opacity-70"
              style={{ shadowColor: '#000', shadowOpacity: 0.05, shadowRadius: 6, shadowOffset: { width: 0, height: 2 }, elevation: 1 }}
              accessibilityRole="button"
              accessibilityLabel={t('a11y.cash')}
            >
              <View className="h-8 w-8 items-center justify-center rounded-lg bg-gold-100">
                <Ionicons name="cash-outline" size={16} color="#8A641D" />
              </View>
              <Text className="text-sm font-bold text-ink">{t('home.cash')}</Text>
              <View className="flex-1" /><Ionicons name="chevron-forward" size={14} color="#A8A493" />
            </Pressable>
          </View>
        </ScrollView>

        {/* CTA */}
        <View className="px-5 pb-3 pt-1">
          <Button
            title={t('home.viewTodaysTasks')}
            iconRight="arrow-forward"
            size="lg"
            fullWidth
            disabled={!isOnDuty}
            onPress={() => router.push('/(app)/tasks')}
          />
        </View>
      </SafeAreaView>
    </View>
  );
}
