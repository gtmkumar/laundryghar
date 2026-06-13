/**
 * Parcel step 4 — "Review & confirm".
 *
 * On mount, fetches a fare quote for the chosen pickup/drop/tier and holds the
 * short-lived token in the booking store. On confirm:
 *   - if the held quote is past its expiresAt, silently re-quote first;
 *   - then create the parcel order with the exact held token;
 *   - on success, reset the parcel stack and jump to order tracking.
 *
 * States covered: quoting, quote error / 422 ("couldn't price this route"),
 * re-quote on expiry, payment selection, create-in-flight, create error.
 */
import React, { useCallback, useEffect, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  Pressable,
  ScrollView,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import { useBookingStore, type PaymentMethod } from '@/store/bookingStore';
import { useWallet } from '@/hooks/useCommerce';
import { useCreateParcelOrder, useFareQuote } from '@/hooks/useOrders';
import { rupees } from '@/lib/format';
import { hapticError, hapticImpact, hapticSuccess } from '@/lib/haptics';
import type { FareQuoteDto } from '@/types/api';

type LivePaymentMethod = Extract<PaymentMethod, 'wallet' | 'cod'>;

const TIER_NAME_KEY: Record<string, string> = {
  two_wheeler: 'parcel.tiers.twoWheelerName',
  three_wheeler: 'parcel.tiers.threeWheelerName',
  four_wheeler: 'parcel.tiers.fourWheelerName',
};

/** True when the quote is missing or its token has expired. */
function isQuoteStale(q: FareQuoteDto | null): boolean {
  if (!q) return true;
  const expiry = Date.parse(q.expiresAt);
  if (Number.isNaN(expiry)) return false; // unparseable — trust the server to 422 if bad
  return Date.now() >= expiry;
}

export default function ParcelQuoteScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const {
    pickupAddress,
    dropAddress,
    vehicleTier,
    fareQuote,
    setFareQuote,
    reset,
  } = useBookingStore();
  const { data: wallet } = useWallet();

  const fareQuoteMut = useFareQuote();
  const createOrder = useCreateParcelOrder();

  const [method, setMethod] = useState<LivePaymentMethod>('wallet');

  // ── Quote on mount (and whenever route/tier changes the held quote away) ──────
  const runQuote = useCallback(async () => {
    if (!pickupAddress?.id || !dropAddress?.id) return;
    try {
      const q = await fareQuoteMut.mutateAsync({
        pickupAddressId: pickupAddress.id,
        deliveryAddressId: dropAddress.id,
        vehicleTier,
      });
      setFareQuote(q);
    } catch {
      // Surfaced via fareQuoteMut.isError below.
    }
  }, [pickupAddress?.id, dropAddress?.id, vehicleTier, fareQuoteMut, setFareQuote]);

  useEffect(() => {
    if (!fareQuote || isQuoteStale(fareQuote)) {
      void runQuote();
    }
    // Run once on mount; runQuote is stable enough and we don't want a re-quote loop.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Wallet can't cover → default to COD (don't fight a valid explicit choice).
  const total = fareQuote?.totalCharge ?? 0;
  const walletBalance = wallet?.balance ?? 0;
  const walletInsufficient = walletBalance < total;
  useEffect(() => {
    if (method === 'wallet' && fareQuote && walletInsufficient) {
      setMethod('cod');
    }
  }, [method, fareQuote, walletInsufficient]);

  const handleConfirm = async () => {
    if (!pickupAddress?.id || !dropAddress?.id) {
      Alert.alert(t('parcel.createFailedTitle'), t('parcel.missingAddresses'));
      return;
    }
    hapticImpact();

    // Ensure a fresh, unexpired quote/token before creating.
    let quote = fareQuote;
    if (isQuoteStale(quote)) {
      try {
        quote = await fareQuoteMut.mutateAsync({
          pickupAddressId: pickupAddress.id,
          deliveryAddressId: dropAddress.id,
          vehicleTier,
        });
        setFareQuote(quote);
        // Tell the user the price refreshed; let them confirm against the new figure.
        Alert.alert(t('parcel.quoteExpired'));
        return;
      } catch {
        hapticError();
        return; // quote error UI is already rendered
      }
    }

    try {
      const order = await createOrder.mutateAsync({
        pickupAddressId: pickupAddress.id,
        deliveryAddressId: dropAddress.id,
        vehicleTier,
        fareQuoteToken: quote!.token,
        paymentPreference: method,
        notesCustomer: null,
      });
      hapticSuccess();
      const orderId = order.id;
      reset();
      router.dismissAll();
      router.replace(`/(app)/orders/tracking/${orderId}` as never);
    } catch (err: unknown) {
      hapticError();
      const msg = err instanceof Error ? err.message : t('parcel.createFailedBody');
      // A 422 here usually means the token expired/mismatched between quote and confirm.
      Alert.alert(t('parcel.createFailedTitle'), msg, [
        { text: t('common.close'), style: 'cancel' },
        { text: t('parcel.retry'), onPress: () => void runQuote() },
      ]);
    }
  };

  const quoting = fareQuoteMut.isPending && !fareQuote;
  const quoteFailed = fareQuoteMut.isError && !fareQuote;
  const surge = fareQuote && fareQuote.surgeMultiplier > 1 ? fareQuote.surgeMultiplier : null;

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      {/* Header */}
      <View className="flex-row items-center gap-3 px-5 pb-3 pt-2">
        <Pressable
          onPress={() => router.back()}
          className="h-10 w-10 items-center justify-center rounded-full bg-white"
          accessibilityRole="button"
          accessibilityLabel={t('a11y.back')}
        >
          <Ionicons name="chevron-back" size={22} color="#3C3F35" />
        </Pressable>
        <Text className="text-xl font-extrabold text-ink">{t('parcel.reviewConfirm')}</Text>
      </View>

      <ScrollView
        showsVerticalScrollIndicator={false}
        contentContainerStyle={{ paddingBottom: 140 }}
      >
        {/* Route summary */}
        <View className="mx-5 rounded-2xl bg-white p-4">
          <Text className="mb-3 text-[11px] font-bold uppercase tracking-wider text-ink-faint">
            {t('parcel.routeSummary')}
          </Text>
          <RouteRow
            icon="navigate-circle-outline"
            tint="#5C6A33"
            label={t('parcel.fromLabel')}
            value={pickupAddress?.line1 ?? '—'}
          />
          <View className="my-1 ml-4 h-4 w-px bg-cream-300" />
          <RouteRow
            icon="flag-outline"
            tint="#8A641D"
            label={t('parcel.toLabel')}
            value={dropAddress?.line1 ?? '—'}
          />
          <View className="mt-3 flex-row items-center justify-between border-t border-cream-200 pt-3">
            <Text className="text-sm text-ink-muted">{t('parcel.vehicleLabel')}</Text>
            <Text className="text-sm font-bold text-ink-soft">
              {t(TIER_NAME_KEY[vehicleTier] ?? 'parcel.tiers.twoWheelerName')}
            </Text>
          </View>
        </View>

        {/* Fare card */}
        {quoting ? (
          <View className="mx-5 mt-4 items-center rounded-2xl bg-white p-8">
            <ActivityIndicator size="small" color="#5C6A33" />
            <Text className="mt-3 text-sm text-ink-muted">{t('parcel.pricing')}</Text>
          </View>
        ) : quoteFailed ? (
          <View className="mx-5 mt-4 rounded-2xl border border-danger/30 bg-red-50 p-5">
            <View className="flex-row items-center gap-2">
              <Ionicons name="alert-circle-outline" size={18} color="#C0492F" />
              <Text className="text-sm font-extrabold text-danger">
                {t('parcel.quoteFailedTitle')}
              </Text>
            </View>
            <Text className="mt-1 text-xs leading-4 text-ink-muted">
              {t('parcel.quoteFailedBody')}
            </Text>
            <Pressable
              onPress={() => void runQuote()}
              className="mt-3 flex-row items-center justify-center gap-2 self-start rounded-full bg-olive-700 px-4 py-2"
              accessibilityRole="button"
              accessibilityLabel={t('parcel.retry')}
            >
              <Ionicons name="refresh" size={14} color="#FFFFFF" />
              <Text className="text-sm font-bold text-white">{t('parcel.retry')}</Text>
            </Pressable>
          </View>
        ) : fareQuote ? (
          <View className="mx-5 mt-4 rounded-2xl bg-white p-4">
            <Text className="mb-3 text-[11px] font-bold uppercase tracking-wider text-ink-faint">
              {t('parcel.fareTitle')}
            </Text>
            <FareRow
              label={t('parcel.distance')}
              value={t('parcel.distanceKm', { km: fareQuote.distanceKm.toFixed(1) })}
            />
            <FareRow label={t('parcel.pickupCharge')} value={rupees(fareQuote.pickupCharge)} />
            <FareRow label={t('parcel.deliveryCharge')} value={rupees(fareQuote.deliveryCharge)} />
            {surge ? (
              <View className="mt-1 flex-row items-center gap-1.5">
                <Ionicons name="trending-up" size={14} color="#C0492F" />
                <Text className="text-xs font-semibold text-danger">
                  {t('parcel.surgeNote', { x: surge.toFixed(1) })}
                </Text>
              </View>
            ) : null}
            <View className="mt-2 flex-row items-center justify-between border-t border-cream-200 pt-3">
              <Text className="text-base font-extrabold text-ink">{t('parcel.total')}</Text>
              <Text className="text-base font-extrabold text-ink">{rupees(fareQuote.totalCharge)}</Text>
            </View>
          </View>
        ) : null}

        {/* Payment method */}
        {fareQuote ? (
          <>
            <Text className="mx-5 mb-3 mt-7 text-[11px] font-bold uppercase tracking-wider text-ink-faint">
              {t('parcel.payWith')}
            </Text>
            <View className="mx-5 gap-3">
              {(['wallet', 'cod'] as LivePaymentMethod[]).map((key) => {
                const selected = method === key;
                const isWalletDisabled = key === 'wallet' && walletInsufficient;
                const title =
                  key === 'wallet'
                    ? t('booking.paymentMethods.wallet')
                    : t('booking.paymentMethods.cod');
                const subtitle = isWalletDisabled
                  ? t('booking.walletInsufficient')
                  : key === 'wallet'
                    ? wallet
                      ? t('booking.paymentMethods.walletBalance', { balance: rupees(walletBalance) })
                      : t('booking.paymentMethods.walletDefault')
                    : t('booking.paymentMethods.codSubtitle');
                return (
                  <Pressable
                    key={key}
                    onPress={() => {
                      if (!isWalletDisabled) {
                        hapticImpact();
                        setMethod(key);
                      }
                    }}
                    disabled={isWalletDisabled}
                    accessibilityRole="radio"
                    accessibilityState={{ selected, disabled: isWalletDisabled }}
                    accessibilityLabel={title}
                    className={[
                      'flex-row items-center rounded-2xl border bg-white p-4',
                      selected ? 'border-olive-600' : 'border-cream-300',
                      isWalletDisabled ? 'opacity-50' : '',
                    ].join(' ')}
                  >
                    <View className="mr-3 h-10 w-10 items-center justify-center rounded-xl bg-cream-100">
                      <Ionicons
                        name={key === 'wallet' ? 'wallet' : 'cash-outline'}
                        size={20}
                        color="#5C6A33"
                      />
                    </View>
                    <View className="flex-1">
                      <Text className="text-base font-bold text-ink">{title}</Text>
                      <Text className="text-xs text-ink-muted">{subtitle}</Text>
                    </View>
                    <View
                      className={`h-5 w-5 items-center justify-center rounded-full border-2 ${
                        selected ? 'border-olive-600' : 'border-cream-400'
                      }`}
                    >
                      {selected ? <View className="h-2.5 w-2.5 rounded-full bg-olive-600" /> : null}
                    </View>
                  </Pressable>
                );
              })}
            </View>
          </>
        ) : null}
      </ScrollView>

      {/* Confirm */}
      <View
        className="absolute inset-x-0 bottom-0 border-t border-cream-200 bg-white px-6 pb-8 pt-4"
        style={{ borderTopLeftRadius: 28, borderTopRightRadius: 28 }}
      >
        <Pressable
          onPress={() => void handleConfirm()}
          disabled={!fareQuote || createOrder.isPending || fareQuoteMut.isPending}
          className={[
            'flex-row items-center justify-center gap-2 rounded-2xl py-4',
            !fareQuote || createOrder.isPending || fareQuoteMut.isPending
              ? 'bg-cream-300'
              : 'bg-olive-700',
          ].join(' ')}
          accessibilityRole="button"
          accessibilityLabel={t('parcel.confirmOrder', { amount: rupees(total) })}
          accessibilityState={{
            disabled: !fareQuote || createOrder.isPending || fareQuoteMut.isPending,
          }}
        >
          {createOrder.isPending ? <ActivityIndicator size="small" color="#FFFFFF" /> : null}
          <Text
            className={`text-base font-extrabold ${
              !fareQuote ? 'text-ink-faint' : 'text-white'
            }`}
          >
            {createOrder.isPending
              ? t('parcel.placing')
              : t('parcel.confirmOrder', { amount: rupees(total) })}
          </Text>
          {fareQuote && !createOrder.isPending ? (
            <Ionicons name="arrow-forward" size={18} color="#FFFFFF" />
          ) : null}
        </Pressable>
      </View>
    </SafeAreaView>
  );
}

function RouteRow({
  icon,
  tint,
  label,
  value,
}: {
  icon: React.ComponentProps<typeof Ionicons>['name'];
  tint: string;
  label: string;
  value: string;
}) {
  return (
    <View className="flex-row items-center gap-3">
      <View className="h-8 w-8 items-center justify-center rounded-xl bg-cream-100">
        <Ionicons name={icon} size={16} color={tint} />
      </View>
      <View className="flex-1">
        <Text className="text-[11px] font-bold uppercase tracking-wider text-ink-faint">{label}</Text>
        <Text className="text-sm font-bold text-ink" numberOfLines={1}>
          {value}
        </Text>
      </View>
    </View>
  );
}

function FareRow({ label, value }: { label: string; value: string }) {
  return (
    <View className="flex-row items-center justify-between py-1.5">
      <Text className="text-sm text-ink-muted">{label}</Text>
      <Text className="text-sm font-bold text-ink-soft">{value}</Text>
    </View>
  );
}
