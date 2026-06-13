/**
 * Payouts & Withdrawals screen.
 *
 * Shows the rider's withdrawable balance (Available, big) with a breakdown
 * (earned payout, incentives, withdrawn/pending), a "Withdraw" action that opens
 * an amount modal (client-side validated against `available`, server-side 422 if
 * it slips through), the withdrawal-request history with status badges, and the
 * incentive/bonus awards earned over the last 30 days.
 *
 * Entry: from the profile screen ("Payouts & Withdrawals" quick link).
 * Data:  useBalance / usePayoutRequests / useIncentives (GET) +
 *        useRequestPayout (POST /rider/payout-requests).
 *
 * Layout/theme mirrors the read-only earnings screen (olive header, cream body,
 * white cards) and the documents screen's StatusBadge + modal patterns.
 */
import React, { useMemo, useState } from 'react';
import {
  ActivityIndicator,
  Modal,
  Pressable,
  RefreshControl,
  ScrollView,
  Text,
  TextInput,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { StatusBar } from 'expo-status-bar';
import {
  useBalance,
  useIncentives,
  usePayoutRequests,
  useRequestPayout,
} from '@/hooks/useEarnings';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { Button } from '@/components/ui/Button';
import type {
  RiderIncentiveDto,
  RiderPayoutRequestDto,
  RiderPayoutRequestStatus,
} from '@/types/api';

const INCENTIVE_DAYS = 30;

// ---------------------------------------------------------------------------
// Formatting helpers
// ---------------------------------------------------------------------------

/** ₹ with thousands separators and two decimals — consistent app-wide. */
function rupees(amount: number): string {
  return `₹${amount.toLocaleString('en-IN', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`;
}

function formatDateTime(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleDateString('en-IN', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

// ---------------------------------------------------------------------------
// Withdrawal-request status badge
// ---------------------------------------------------------------------------

// requested = amber, approved = blue, paid = green, rejected = red.
// `info` (#3F6E8C) is the on-palette blue; we tint it via inline rgba because
// the Tailwind config has no `blue-*` ramp, so NativeWind wouldn't emit those.
const STATUS_META: Record<
  RiderPayoutRequestStatus,
  { label: string; bg: string; fg: string; icon: React.ComponentProps<typeof Ionicons>['name'] }
> = {
  requested: { label: 'Requested', bg: 'rgba(204,154,44,0.15)',  fg: '#8A641D', icon: 'time' },
  approved:  { label: 'Approved',  bg: 'rgba(63,110,140,0.15)',  fg: '#2F5468', icon: 'checkmark-circle' },
  paid:      { label: 'Paid',      bg: 'rgba(74,85,42,0.15)',    fg: '#4A552A', icon: 'cash' },
  rejected:  { label: 'Rejected',  bg: 'rgba(192,73,47,0.12)',   fg: '#C0492F', icon: 'close-circle' },
};

function StatusBadge({ status }: { status: RiderPayoutRequestStatus }) {
  const s = STATUS_META[status] ?? STATUS_META.requested;
  return (
    <View
      className="flex-row items-center gap-1 rounded-full px-3 py-1"
      style={{ backgroundColor: s.bg }}
    >
      <Ionicons name={s.icon} size={12} color={s.fg} />
      <Text className="text-[11px] font-bold" style={{ color: s.fg }}>{s.label}</Text>
    </View>
  );
}

// ---------------------------------------------------------------------------
// Rows
// ---------------------------------------------------------------------------

function BreakdownRow({
  label,
  value,
  muted,
}: {
  label: string;
  value: string;
  muted?: boolean;
}) {
  return (
    <View className="flex-row items-center justify-between py-1.5">
      <Text className="text-xs text-olive-100">{label}</Text>
      <Text className={`text-sm font-bold ${muted ? 'text-olive-100' : 'text-white'}`}>{value}</Text>
    </View>
  );
}

function RequestRow({ item }: { item: RiderPayoutRequestDto }) {
  return (
    <View
      className="mb-3 rounded-3xl bg-white p-4"
      style={{ shadowColor: '#000', shadowOpacity: 0.05, shadowRadius: 8, shadowOffset: { width: 0, height: 3 }, elevation: 2 }}
    >
      <View className="flex-row items-center justify-between">
        <Text className="text-base font-extrabold text-ink">{rupees(item.amount)}</Text>
        <StatusBadge status={item.status} />
      </View>

      <Text className="mt-1 text-[11px] text-ink-muted">
        Requested {formatDateTime(item.requestedAt)}
      </Text>

      {item.status === 'rejected' && item.rejectionReason ? (
        <View className="mt-3 rounded-2xl bg-danger/10 px-3 py-2">
          <Text className="text-[11px] font-semibold text-danger">
            Rejected: {item.rejectionReason}
          </Text>
        </View>
      ) : null}

      {item.status === 'paid' && item.paymentReference ? (
        <View className="mt-3 rounded-2xl bg-olive-50 px-3 py-2">
          <Text className="text-[11px] font-semibold text-olive-800">
            Reference: {item.paymentReference}
          </Text>
          {item.paidAt ? (
            <Text className="mt-0.5 text-[10px] text-ink-muted">
              Paid {formatDateTime(item.paidAt)}
            </Text>
          ) : null}
        </View>
      ) : null}
    </View>
  );
}

function IncentiveRow({ item }: { item: RiderIncentiveDto }) {
  return (
    <View
      className="mb-3 flex-row items-center rounded-2xl bg-white px-4 py-3.5"
      style={{ elevation: 1 }}
    >
      <View className="h-9 w-9 items-center justify-center rounded-full bg-gold-100">
        <Ionicons name="gift-outline" size={18} color="#8A641D" />
      </View>
      <View className="ml-3 flex-1">
        <Text className="text-sm font-bold text-ink" numberOfLines={1}>
          {item.ruleName}
        </Text>
        <Text className="mt-0.5 text-[11px] text-ink-muted">
          {formatDateTime(item.awardedAt)}
        </Text>
      </View>
      <Text className="ml-2 text-sm font-extrabold text-gold-700">+{rupees(item.amount)}</Text>
    </View>
  );
}

// ---------------------------------------------------------------------------
// Section header
// ---------------------------------------------------------------------------

function SectionLabel({ children }: { children: React.ReactNode }) {
  return (
    <Text className="mb-3 ml-1 mt-5 text-xs font-bold uppercase tracking-widest text-ink-muted">
      {children}
    </Text>
  );
}

// ---------------------------------------------------------------------------
// Withdraw modal
// ---------------------------------------------------------------------------

function WithdrawModal({
  visible,
  available,
  submitting,
  serverError,
  onClose,
  onSubmit,
}: {
  visible: boolean;
  available: number;
  submitting: boolean;
  serverError: string | null;
  onClose: () => void;
  onSubmit: (amount: number) => void;
}) {
  const [raw, setRaw] = useState('');

  // Reset the field each time the modal opens.
  React.useEffect(() => {
    if (visible) setRaw('');
  }, [visible]);

  const parsed = Number(raw);
  const isValidNumber = raw.trim() !== '' && Number.isFinite(parsed) && parsed > 0;
  const exceeds = isValidNumber && parsed > available;
  const canSubmit = isValidNumber && !exceeds && !submitting;

  const clientError = !isValidNumber
    ? null
    : exceeds
      ? 'Amount exceeds your available balance.'
      : null;

  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
      <View className="flex-1 justify-end bg-black/40">
        <View className="rounded-t-3xl bg-cream px-5 pb-8 pt-5">
          <Text className="mb-1 text-center text-base font-extrabold text-ink">
            Withdraw earnings
          </Text>
          <Text className="mb-4 text-center text-xs text-ink-muted">
            Available {rupees(available)}
          </Text>

          <View className="flex-row items-center rounded-2xl bg-white px-4 py-1">
            <Text className="text-2xl font-extrabold text-ink">₹</Text>
            <TextInput
              value={raw}
              onChangeText={(v) => setRaw(v.replace(/[^0-9.]/g, ''))}
              keyboardType="decimal-pad"
              placeholder="0"
              placeholderTextColor="#A8A493"
              autoFocus
              editable={!submitting}
              className="ml-1 flex-1 py-3 text-2xl font-extrabold text-ink"
              accessibilityLabel="Withdrawal amount in rupees"
              returnKeyType="done"
              onSubmitEditing={() => { if (canSubmit) onSubmit(parsed); }}
            />
            <Pressable
              onPress={() => setRaw(String(available))}
              hitSlop={6}
              className="rounded-full bg-olive-100 px-3 py-1.5 active:opacity-70"
              accessibilityRole="button"
              accessibilityLabel="Withdraw the full available balance"
            >
              <Text className="text-xs font-bold text-olive-800">Max</Text>
            </Pressable>
          </View>

          {clientError || serverError ? (
            <Text className="mt-2 px-1 text-xs font-semibold text-danger">
              {clientError ?? serverError}
            </Text>
          ) : null}

          <View className="mt-5 gap-2">
            <Button
              title="Request withdrawal"
              variant="olive"
              fullWidth
              loading={submitting}
              disabled={!canSubmit}
              onPress={() => onSubmit(parsed)}
            />
            <Button
              title="Cancel"
              variant="ghost"
              fullWidth
              disabled={submitting}
              onPress={onClose}
            />
          </View>
        </View>
      </View>
    </Modal>
  );
}

// ---------------------------------------------------------------------------
// Screen
// ---------------------------------------------------------------------------

export default function PayoutsScreen() {
  const router = useRouter();

  const balance  = useBalance();
  const requests = usePayoutRequests();
  const incentives = useIncentives(INCENTIVE_DAYS);
  const requestPayout = useRequestPayout();

  const [modalOpen, setModalOpen] = useState(false);

  const available = balance.data?.available ?? 0;

  const incentiveTotal = useMemo(
    () => (incentives.data ?? []).reduce((sum, i) => sum + i.amount, 0),
    [incentives.data],
  );

  async function handleSubmit(amount: number) {
    requestPayout.reset();
    try {
      await requestPayout.mutateAsync(amount);
      // onSuccess in the hook refetches balance + requests.
      setModalOpen(false);
    } catch {
      // Stay open; the 422/other message renders inside the modal.
    }
  }

  function openModal() {
    requestPayout.reset();
    setModalOpen(true);
  }

  function refetchAll() {
    void balance.refetch();
    void requests.refetch();
    void incentives.refetch();
  }

  // Block only on the primary (balance) query's first load.
  if (balance.isLoading && !balance.data) return <ScreenLoader />;

  const serverError =
    requestPayout.isError && requestPayout.error instanceof Error
      ? requestPayout.error.message
      : null;

  const refreshing =
    balance.isRefetching || requests.isRefetching || incentives.isRefetching;

  const canWithdraw = available > 0;

  return (
    <View className="flex-1 bg-cream">
      <StatusBar style="light" />

      {/* Olive header with balance */}
      <SafeAreaView edges={['top']} style={{ backgroundColor: '#4A552A' }}>
        <View className="px-5 pb-6 pt-2">
          <View className="flex-row items-center">
            <Pressable
              onPress={() => router.back()}
              hitSlop={8}
              className="mr-2 h-9 w-9 items-center justify-center active:opacity-60"
              accessibilityLabel="Go back"
              accessibilityRole="button"
            >
              <Ionicons name="chevron-back" size={24} color="#FFFFFF" />
            </Pressable>
            <Text className="flex-1 text-base font-extrabold text-white">
              Payouts & Withdrawals
            </Text>
          </View>

          {balance.isError && !balance.data ? (
            <View className="mt-5">
              <Text className="text-sm font-semibold text-olive-100">
                Could not load your balance. Pull to refresh.
              </Text>
            </View>
          ) : (
            <>
              <Text className="mt-5 text-xs uppercase tracking-widest text-olive-200">
                Available to withdraw
              </Text>
              <Text className="mt-1 text-4xl font-extrabold text-white">
                {rupees(available)}
              </Text>

              <View className="mt-4 rounded-2xl bg-white/10 px-4 py-2">
                <BreakdownRow label="Earned payout" value={rupees(balance.data?.earnedPayout ?? 0)} />
                <BreakdownRow label="Incentives" value={rupees(balance.data?.incentives ?? 0)} />
                <BreakdownRow
                  label="Withdrawn / pending"
                  value={`− ${rupees(balance.data?.withdrawnOrPending ?? 0)}`}
                  muted
                />
              </View>

              <View className="mt-4">
                <Button
                  title="Withdraw"
                  variant="primary"
                  fullWidth
                  iconLeft="arrow-down-circle-outline"
                  disabled={!canWithdraw}
                  onPress={openModal}
                />
                {!canWithdraw ? (
                  <Text className="mt-2 text-center text-[11px] text-olive-200">
                    No balance available to withdraw yet.
                  </Text>
                ) : null}
              </View>
            </>
          )}
        </View>
      </SafeAreaView>

      <ScrollView
        contentContainerStyle={{ paddingHorizontal: 20, paddingBottom: 40 }}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={refetchAll}
            tintColor="#4A552A"
            colors={['#4A552A']}
          />
        }
      >
        {/* Withdrawal history */}
        <SectionLabel>Withdrawal history</SectionLabel>

        {requests.isLoading && !requests.data ? (
          <View className="items-center py-6">
            <ActivityIndicator size="small" color="#4A552A" />
          </View>
        ) : requests.isError ? (
          <ErrorState
            message="Could not load your withdrawals."
            onRetry={() => void requests.refetch()}
          />
        ) : (requests.data ?? []).length === 0 ? (
          <View className="items-center rounded-3xl bg-white px-6 py-8" style={{ elevation: 1 }}>
            <View className="h-12 w-12 items-center justify-center rounded-full bg-olive-100">
              <Ionicons name="receipt-outline" size={22} color="#4A552A" />
            </View>
            <Text className="mt-3 text-sm font-bold text-ink">No withdrawals yet</Text>
            <Text className="mt-1 text-center text-xs text-ink-muted">
              Request a withdrawal of your available balance to see it here.
            </Text>
          </View>
        ) : (
          (requests.data ?? []).map((item) => <RequestRow key={item.id} item={item} />)
        )}

        {/* Incentives */}
        <View className="mt-2 flex-row items-center justify-between">
          <SectionLabel>Incentives (last {INCENTIVE_DAYS} days)</SectionLabel>
          {(incentives.data ?? []).length > 0 ? (
            <Text className="mt-5 text-sm font-extrabold text-gold-700">
              +{rupees(incentiveTotal)}
            </Text>
          ) : null}
        </View>

        {incentives.isLoading && !incentives.data ? (
          <View className="items-center py-6">
            <ActivityIndicator size="small" color="#8A641D" />
          </View>
        ) : incentives.isError ? (
          <ErrorState
            message="Could not load incentives."
            onRetry={() => void incentives.refetch()}
          />
        ) : (incentives.data ?? []).length === 0 ? (
          <View className="items-center rounded-3xl bg-white px-6 py-8" style={{ elevation: 1 }}>
            <View className="h-12 w-12 items-center justify-center rounded-full bg-gold-100">
              <Ionicons name="gift-outline" size={22} color="#8A641D" />
            </View>
            <Text className="mt-3 text-sm font-bold text-ink">No bonuses yet</Text>
            <Text className="mt-1 text-center text-xs text-ink-muted">
              Earn incentives by hitting delivery targets and streaks.
            </Text>
          </View>
        ) : (
          (incentives.data ?? []).map((item) => <IncentiveRow key={item.id} item={item} />)
        )}
      </ScrollView>

      <WithdrawModal
        visible={modalOpen}
        available={available}
        submitting={requestPayout.isPending}
        serverError={serverError}
        onClose={() => setModalOpen(false)}
        onSubmit={(amount) => void handleSubmit(amount)}
      />
    </View>
  );
}
