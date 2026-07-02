/**
 * Booking step 3 — "Payment".
 * Order summary + coupon entry + payment-method picker.
 *
 * R3-BE-2: Apply coupon flow
 *   - TextInput to enter/paste a coupon code.
 *   - "Quick pick" from the customer's offers (GET /customer/coupons via Commerce).
 *   - Validates via POST /customer/coupons/validate (Orders service preview endpoint).
 *   - Shows discount line in summary on success; friendly inline error on invalid.
 *   - Passes couponCode in the pickup-request create payload.
 *   - Wallet is now a payment METHOD only — the fake 10% wallet discount is removed.
 *   - Haptic on coupon apply success/error.
 *
 * R3-MOB-1: KeyboardAvoidingView wraps the whole screen so the coupon TextInput
 *   is not obscured by the Android software keyboard.
 */
import React, { useCallback, useMemo, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  Text,
  TextInput,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import { useCartStore } from '@/store/cartStore';
import { useBookingStore, type PaymentMethod } from '@/store/bookingStore';
import { useWallet } from '@/hooks/useCommerce';
import { useCoupons } from '@/hooks/useCommerce';
import { useCatalogConfig } from '@/hooks/useCatalog';
import { useSchedulePickup, useValidateCoupon } from '@/hooks/useOrders';
import { localDateIso, rupees } from '@/lib/format';
import { evaluateMinOrder, formatCurrency, parseMinOrderError } from '@/lib/minOrder';
import { parseValueSlabError } from '@/lib/valueSlab';
import { MinOrderSheet } from '@/components/ui/MinOrderSheet';
import { DeclaredValueSheet } from '@/components/ui/DeclaredValueSheet';
import { hapticError, hapticImpact, hapticSuccess, hapticWarning } from '@/lib/haptics';
import { EXPRESS_SURCHARGE, FEATURES } from '@/constants/config';

/** State backing the blocking minimum-order sheet. */
interface MinOrderPrompt {
  subtotal: number;
  minimum: number;
  shortfall: number;
  currencyCode: string;
}

/** State backing the declared-value re-prompt after a value-slab 422 (GH #22). */
interface DeclaredValuePrompt {
  /** Cart-line key (CartLine.id) to update. */
  lineId: string;
  itemName: string;
  initial?: number;
  /** Friendly server reason naming the item. */
  message: string;
}

/** Only the two methods that are live today. */
type LivePaymentMethod = Extract<PaymentMethod, 'wallet' | 'cod'>;

interface MethodMeta {
  key: LivePaymentMethod;
  title: string;
  subtitle: string;
  icon: React.ComponentProps<typeof Ionicons>['name'];
}

/** Map app payment method keys to the backend's payment_preference values. */
function toPaymentPreference(method: LivePaymentMethod): string {
  if (method === 'wallet') return 'wallet';
  return 'cod';
}

// ── Coupon bar ────────────────────────────────────────────────────────────────

interface CouponBarProps {
  subtotal: number;
  appliedCode: string | null;
  discountAmount: number;
  onApply: (code: string, discount: number) => void;
  onRemove: () => void;
}

function CouponBar({ subtotal, appliedCode, discountAmount, onApply, onRemove }: CouponBarProps) {
  const { t } = useTranslation();
  const [input, setInput] = useState(appliedCode ?? '');
  const [error, setError] = useState<string | null>(null);
  const { data: availableCoupons } = useCoupons();
  const validateMutation = useValidateCoupon();

  const handleApply = useCallback(async () => {
    const code = input.trim().toUpperCase();
    if (!code) return;
    setError(null);
    try {
      const result = await validateMutation.mutateAsync({ couponCode: code, estimatedSubtotal: subtotal });
      if (result.valid) {
        hapticSuccess();
        onApply(code, result.discountPreview);
      } else {
        hapticError();
        setError(result.reason ?? t('booking.coupon.invalid'));
        onRemove();
      }
    } catch {
      hapticError();
      setError(t('booking.coupon.validationFailed'));
      onRemove();
    }
  }, [input, subtotal, validateMutation, onApply, onRemove, t]);

  const handleQuickPick = useCallback((code: string) => {
    hapticImpact();
    setInput(code);
    setError(null);
  }, []);

  return (
    <View className="mx-5 mt-4">
      <Text className="mb-2 text-[11px] font-bold uppercase tracking-wider text-ink-faint">
        {t('booking.coupon.label')}
      </Text>

      {/* Quick-pick chips */}
      {availableCoupons && availableCoupons.length > 0 ? (
        <ScrollView horizontal showsHorizontalScrollIndicator={false} className="mb-3 -mx-1">
          <View className="flex-row gap-2 px-1 py-0.5">
            {availableCoupons.slice(0, 6).map((c) => (
              <Pressable
                key={c.id}
                onPress={() => handleQuickPick(c.code)}
                accessibilityRole="button"
                accessibilityLabel={`Apply coupon ${c.code}`}
                className={[
                  'rounded-full border px-3 py-1.5',
                  appliedCode === c.code
                    ? 'border-olive-600 bg-olive-600'
                    : 'border-cream-300 bg-white',
                ].join(' ')}
              >
                <Text className={`text-xs font-bold ${appliedCode === c.code ? 'text-white' : 'text-olive-700'}`}>
                  {c.code}
                </Text>
              </Pressable>
            ))}
          </View>
        </ScrollView>
      ) : null}

      {/* Applied banner or input row */}
      {appliedCode ? (
        <View className="flex-row items-center rounded-2xl border border-success bg-green-50 px-4 py-3">
          <Ionicons name="checkmark-circle" size={18} color="#4F8A4F" style={{ marginRight: 8 }} />
          <View className="flex-1">
            <Text className="text-sm font-bold text-success">{appliedCode}</Text>
            <Text className="text-xs text-success">{t('booking.coupon.saves', { amount: rupees(discountAmount) })}</Text>
          </View>
          <Pressable
            onPress={() => { onRemove(); setInput(''); setError(null); }}
            hitSlop={8}
            accessibilityRole="button"
            accessibilityLabel={t('booking.coupon.remove')}
          >
            <Ionicons name="close-circle" size={20} color="#4F8A4F" />
          </Pressable>
        </View>
      ) : (
        <View className="flex-row items-center gap-2">
          <View className="flex-1 flex-row items-center rounded-2xl border border-cream-300 bg-white px-4 py-2.5">
            <Ionicons name="pricetag-outline" size={16} color="#A8A493" style={{ marginRight: 8 }} />
            <TextInput
              value={input}
              onChangeText={(v) => { setInput(v.toUpperCase()); setError(null); }}
              placeholder={t('booking.coupon.placeholder')}
              placeholderTextColor="#A8A493"
              autoCapitalize="characters"
              returnKeyType="done"
              onSubmitEditing={() => void handleApply()}
              className="flex-1 text-sm font-bold text-ink"
              accessibilityLabel={t('booking.coupon.placeholder')}
            />
          </View>
          <Pressable
            onPress={() => void handleApply()}
            disabled={validateMutation.isPending || !input.trim()}
            className={[
              'rounded-2xl px-4 py-3',
              validateMutation.isPending || !input.trim() ? 'bg-cream-300' : 'bg-olive-700',
            ].join(' ')}
            accessibilityRole="button"
            accessibilityLabel={t('booking.coupon.apply')}
            accessibilityState={{ disabled: validateMutation.isPending || !input.trim() }}
          >
            {validateMutation.isPending
              ? <ActivityIndicator size="small" color="#FFFFFF" />
              : <Text className={`text-sm font-bold ${!input.trim() ? 'text-ink-faint' : 'text-white'}`}>{t('booking.coupon.apply')}</Text>
            }
          </Pressable>
        </View>
      )}

      {error ? (
        <View className="mt-2 flex-row items-center gap-1.5">
          <Ionicons name="alert-circle-outline" size={14} color="#C0492F" />
          <Text className="text-xs text-danger">{error}</Text>
        </View>
      ) : null}
    </View>
  );
}

// ── Screen ────────────────────────────────────────────────────────────────────

export default function PayScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const { dateLabel } = useLocalSearchParams<{ dateLabel?: string }>();

  // Select raw state scalars — never fresh objects/arrays from selector (zustand v5 rule).
  const lines = useCartStore((s) => s.lines);
  const clearCart = useCartStore((s) => s.clear);
  const setDeclaredValue = useCartStore((s) => s.setDeclaredValue);
  const { express, slot, address, paymentMethod, setPaymentMethod, setConfirmed } = useBookingStore();
  const { data: wallet, isError: walletError } = useWallet();
  // #23: brand min-order config. Laundry flow has no store selection ⇒ brand default.
  const { data: catalogConfig } = useCatalogConfig();

  const [loading, setLoading] = useState(false);
  // #23: blocking minimum-order sheet (client gate or server 422 backstop).
  const [minOrderPrompt, setMinOrderPrompt] = useState<MinOrderPrompt | null>(null);
  // GH #22: declared-value re-prompt after a value-slab 422 rejection.
  const [declaredValuePrompt, setDeclaredValuePrompt] = useState<DeclaredValuePrompt | null>(null);
  // R3-BE-2: coupon state
  const [appliedCoupon, setAppliedCoupon] = useState<string | null>(null);
  const [couponDiscount, setCouponDiscount] = useState(0);

  const schedulePickup = useSchedulePickup();

  // Coerce any legacy upi/card store state to cod so no silent downgrade persists.
  const liveMethod: LivePaymentMethod =
    paymentMethod === 'wallet' ? 'wallet' : 'cod';

  // Derive values with useMemo — no fresh objects from selectors (zustand v5 rule).
  const lineList = useMemo(() => Object.values(lines), [lines]);
  const count = useMemo(() => lineList.reduce((n, l) => n + l.qty, 0), [lineList]);
  // GH #22: value-slab lines carry a placeholder unitPrice (priced from the declared
  // value server-side), so they are excluded from the money subtotal / display total.
  const subtotal = useMemo(
    () => lineList.reduce((sum, l) => (l.pricingMode === 'value_slab' ? sum : sum + l.qty * l.unitPrice), 0),
    [lineList],
  );
  const hasValueSlab = useMemo(() => lineList.some((l) => l.pricingMode === 'value_slab'), [lineList]);

  const expressFee = express ? EXPRESS_SURCHARGE : 0;
  // R3-BE-2: coupon discount only (no fake wallet 10% any more — wallet is a payment method)
  const total = Math.max(0, subtotal + expressFee - couponDiscount);

  // #23: gate on the ITEM subtotal (pre-express, pre-coupon) — matches the
  // backend's min-order comparison basis. null ⇒ no restriction configured.
  // GH #22: suppressed when value-slab lines are present — their price is unknown
  // client-side so the true order total can't be evaluated here; the server 422
  // backstop still enforces the minimum.
  const minGate = useMemo(() => evaluateMinOrder(subtotal, catalogConfig), [subtotal, catalogConfig]);
  const minBlocked = (minGate?.blocked ?? false) && !hasValueSlab;

  const walletBalance = wallet?.balance ?? 0;
  const walletInsufficient = liveMethod === 'wallet' && walletBalance < total;

  // UX: never pre-select a method the user cannot use. When the wallet balance
  // cannot cover the total, fall back to COD automatically (wallet stays
  // visible but disabled, so this cannot fight a valid user choice).
  React.useEffect(() => {
    const cannotCover = wallet ? wallet.balance < total : walletError;
    if (liveMethod === 'wallet' && cannotCover) {
      setPaymentMethod('cod');
    }
  }, [wallet, walletError, liveMethod, total, setPaymentMethod]);

  const methods: MethodMeta[] = useMemo(
    () => [
      {
        key: 'wallet',
        title: t('booking.paymentMethods.wallet'),
        subtitle: wallet
          ? t('booking.paymentMethods.walletBalance', { balance: rupees(wallet.balance) })
          : t('booking.paymentMethods.walletDefault'),
        icon: 'wallet',
      },
      {
        key: 'cod',
        title: t('booking.paymentMethods.cod'),
        subtitle: t('booking.paymentMethods.codSubtitle'),
        icon: 'cash-outline',
      },
    ] satisfies MethodMeta[],
    [wallet, t],
  );

  const handleCouponApply = useCallback((code: string, discount: number) => {
    setAppliedCoupon(code);
    setCouponDiscount(discount);
  }, []);

  const handleCouponRemove = useCallback(() => {
    setAppliedCoupon(null);
    setCouponDiscount(0);
  }, []);

  /**
   * Navigate to the confirmation screen with the booking stack RESET.
   * dismissAll() pops items→pickup→pay off the stack first, so hardware back
   * (or swipe) from the confirmation lands on the tabs, not a stale pay screen.
   */
  const goToConfirm = useCallback(() => {
    try {
      router.dismissAll();
    } catch {
      // Nothing to dismiss — e.g. deep-linked straight to pay. Safe to ignore.
    }
    router.push('/(app)/booking/confirm');
  }, [router]);

  /** #23: return to the item picker to add more (booking selections persist in the store). */
  const handleAddMore = useCallback(() => {
    setMinOrderPrompt(null);
    try {
      router.dismissAll();
    } catch {
      // Nothing to dismiss — safe to ignore.
    }
    router.push('/(app)/booking/items');
  }, [router]);

  const handlePay = async () => {
    if (count === 0) {
      hapticWarning();
      Alert.alert(t('booking.emptyCart'), t('booking.emptyCartMessage'));
      return;
    }

    // #23: block below-minimum orders before any network call.
    if (minBlocked && minGate) {
      hapticWarning();
      setMinOrderPrompt({
        subtotal,
        minimum: minGate.minOrderValue,
        shortfall: minGate.shortfall,
        currencyCode: minGate.currencyCode,
      });
      return;
    }

    hapticImpact();

    if (FEATURES.bookingApi) {
      // ── Live API path ───────────────────────────────────────────────────────
      if (!address?.id) {
        Alert.alert(t('booking.noAddress'), t('booking.noAddressMessage'));
        return;
      }

      const pickupDateIso = slot?.date ?? localDateIso();
      const [winStart, winEnd] = slot
        ? [slot.windowStart ?? '09:00:00', slot.windowEnd ?? '21:00:00']
        : ['09:00:00', '21:00:00'];

      // Send the REAL catalog ids carried on the cart line (the line's own id is
      // the price-list ROW id — the backend 422s that). l.name is already the
      // resolved display label (displayLabel ?? itemName · serviceName).
      const cartItems = lineList.map((l) => ({
        serviceId: l.serviceId,
        itemId: l.itemId,
        displayLabel: l.name,
        quantity: l.qty,
        // GH #22: a value-slab line's unitPrice is a placeholder — send no estimate and
        // the declared value instead, so the server resolves the real price from slabs.
        estimatedUnitPrice: l.pricingMode === 'value_slab' ? null : l.unitPrice,
        declaredValue: l.pricingMode === 'value_slab' ? l.declaredValue ?? null : null,
      }));

      setLoading(true);
      try {
        const result = await schedulePickup.mutateAsync({
          addressId: address.id,
          slotId: slot?.id ?? null,
          pickupDate: pickupDateIso,
          pickupWindowStart: winStart,
          pickupWindowEnd: winEnd,
          isExpress: express,
          estimatedItems: count,
          estimatedAmount: total,
          servicesRequested: [],
          customerNotes: null,
          cartItems,
          paymentPreference: toPaymentPreference(liveMethod),
          // R3-BE-2: pass validated coupon code to the server
          couponCode: appliedCoupon ?? null,
        });

        setConfirmed({
          orderNumber: result.requestNumber,
          pickupRequestId: result.id,
          address: address.line1,
          slotLabel: slot?.label ?? `${winStart.slice(0, 5)} – ${winEnd.slice(0, 5)}`,
          dateLabel: dateLabel ?? pickupDateIso,
          itemCount: count,
          express,
          amount: total,
          paymentMethod: liveMethod,
        });
        clearCart();
        goToConfirm();
      } catch (err: unknown) {
        // #23: defensive backstop — a stale-config 422 maps to the same sheet.
        const violation = parseMinOrderError(err);
        if (violation) {
          hapticWarning();
          setMinOrderPrompt({
            subtotal: violation.subtotal || subtotal,
            minimum: violation.minimum,
            shortfall: violation.shortfall || Math.max(0, violation.minimum - subtotal),
            currencyCode: catalogConfig?.currencyCode ?? '',
          });
          return;
        }
        // GH #22: a value-slab rejection (missing/unmatched declared value) re-opens the
        // declared-value prompt for the named item so the customer can fix it inline.
        const slabError = parseValueSlabError(err);
        if (slabError) {
          hapticWarning();
          const offending = lineList.find((l) => l.itemId != null && l.itemId === slabError.itemId);
          if (offending) {
            setDeclaredValuePrompt({
              lineId: offending.id,
              itemName: slabError.itemName ?? offending.name,
              initial: offending.declaredValue,
              message:
                slabError.message ??
                (slabError.code === 'no_value_slab_match'
                  ? t('booking.valueSlab.errorNoMatch', { item: slabError.itemName ?? offending.name })
                  : t('booking.valueSlab.errorRequired', { item: slabError.itemName ?? offending.name })),
            });
          } else {
            Alert.alert(
              t('booking.valueSlab.errorTitle'),
              slabError.message ?? t('booking.valueSlab.errorRequired', { item: slabError.itemName ?? '' }),
            );
          }
          return;
        }
        hapticError();
        const msg = err instanceof Error ? err.message : t('booking.bookingFailedMessage');
        Alert.alert(t('booking.bookingFailed'), msg);
      } finally {
        setLoading(false);
      }
    } else {
      // ── Local fallback (FEATURES.bookingApi=false) ──────────────────────────
      const orderNumber = `LG-${Math.floor(10000 + Math.random() * 90000)}`;
      setConfirmed({
        orderNumber,
        address: address?.line1 ?? 'DLF Phase 4',
        slotLabel: slot?.label ?? '12 – 2 PM',
        dateLabel: dateLabel ?? 'Today',
        itemCount: count,
        express,
        amount: total,
        paymentMethod: liveMethod,
      });
      clearCart();
      goToConfirm();
    }
  };

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
        <Text className="text-xl font-extrabold text-ink">{t('booking.payment')}</Text>
      </View>

      {/* R3-MOB-1: keyboard avoiding wrapper */}
      <KeyboardAvoidingView
        className="flex-1"
        behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
        keyboardVerticalOffset={0}
      >
        <ScrollView
          showsVerticalScrollIndicator={false}
          contentContainerStyle={{ paddingBottom: 130 }}
          keyboardShouldPersistTaps="handled"
        >
          {/* Summary */}
          <View className="mx-5 rounded-2xl bg-white p-4">
            <Text className="mb-3 text-[11px] font-bold uppercase tracking-wider text-ink-faint">{t('booking.orderSummary')}</Text>
            <SummaryRow label={t('booking.garmentCount', { count, plural: count !== 1 ? 's' : '' })} value={rupees(subtotal)} />
            {express ? <SummaryRow label={t('booking.expressPlus')} value={rupees(expressFee)} /> : null}
            {couponDiscount > 0 ? (
              <SummaryRow
                label={t('booking.coupon.discountLine', { code: appliedCoupon ?? '' })}
                value={`−${rupees(couponDiscount)}`}
                accent
              />
            ) : null}
            <View className="mt-2 flex-row items-center justify-between border-t border-cream-200 pt-3">
              <Text className="text-base font-extrabold text-ink">{t('booking.totalToPay')}</Text>
              <Text className="text-base font-extrabold text-ink">{rupees(total)}</Text>
            </View>
            {/* GH #22: value-slab items aren't in the total — priced at pickup from declared value. */}
            {hasValueSlab ? (
              <View className="mt-2.5 flex-row items-start gap-1.5">
                <Ionicons name="diamond-outline" size={13} color="#8A641D" style={{ marginTop: 1 }} />
                <Text className="flex-1 text-[11px] leading-4 text-gold-700">
                  {t('booking.valueSlab.paySummaryNote')}
                </Text>
              </View>
            ) : null}
          </View>

          {/* R3-BE-2: Coupon entry */}
          <CouponBar
            subtotal={subtotal}
            appliedCode={appliedCoupon}
            discountAmount={couponDiscount}
            onApply={handleCouponApply}
            onRemove={handleCouponRemove}
          />

          {/* Methods */}
          <Text className="mx-5 mb-3 mt-7 text-[11px] font-bold uppercase tracking-wider text-ink-faint">{t('booking.payWith')}</Text>
          <View className="mx-5 gap-3">
            {methods.map((m) => {
              const selected = liveMethod === m.key;
              const isWalletDisabled = m.key === 'wallet' && (wallet?.balance ?? 0) < total;
              return (
                <Pressable
                  key={m.key}
                  onPress={() => {
                    if (!isWalletDisabled) {
                      hapticImpact();
                      setPaymentMethod(m.key);
                    }
                  }}
                  disabled={isWalletDisabled}
                  accessibilityRole="radio"
                  accessibilityState={{ selected, disabled: isWalletDisabled }}
                  accessibilityLabel={`${m.title}${isWalletDisabled ? ' - ' + t('booking.walletInsufficient') : ''}`}
                  className={[
                    'flex-row items-center rounded-2xl border bg-white p-4',
                    selected ? 'border-olive-600' : 'border-cream-300',
                    isWalletDisabled ? 'opacity-50' : '',
                  ].join(' ')}
                >
                  <View className="mr-3 h-10 w-10 items-center justify-center rounded-xl bg-cream-100">
                    <Ionicons name={m.icon} size={20} color="#5C6A33" />
                  </View>
                  <View className="flex-1">
                    <Text className="text-base font-bold text-ink">{m.title}</Text>
                    <Text className="text-xs text-ink-muted">
                      {isWalletDisabled ? t('booking.walletInsufficient') : m.subtitle}
                    </Text>
                  </View>
                  <View className={`h-5 w-5 items-center justify-center rounded-full border-2 ${selected ? 'border-olive-600' : 'border-cream-400'}`}>
                    {selected ? <View className="h-2.5 w-2.5 rounded-full bg-olive-600" /> : null}
                  </View>
                </Pressable>
              );
            })}
          </View>

          {/* Coming-soon info row — UPI / Card */}
          <View className="mx-5 mt-3 flex-row items-start gap-3 rounded-2xl border border-cream-200 bg-white px-4 py-3">
            <Ionicons name="time-outline" size={18} color="#A8A493" style={{ marginTop: 1 }} />
            <View className="flex-1">
              <Text className="text-sm font-bold text-ink-muted">{t('booking.onlinePaymentComingSoonTitle')}</Text>
              <Text className="mt-0.5 text-xs leading-4 text-ink-faint">
                {t('booking.onlinePaymentComingSoonBody')}
              </Text>
            </View>
          </View>
        </ScrollView>
      </KeyboardAvoidingView>

      {/* Pay */}
      <View
        className="absolute inset-x-0 bottom-0 border-t border-cream-200 bg-white px-6 pb-8 pt-4"
        style={{ borderTopLeftRadius: 28, borderTopRightRadius: 28 }}
      >
        {/* #23: min-order hint takes priority — it explains why Pay is blocked. */}
        {minBlocked && minGate ? (
          <View className="mb-3 flex-row items-center gap-2 rounded-xl bg-gold-100 px-4 py-2.5">
            <Ionicons name="information-circle-outline" size={16} color="#8A641D" />
            <Text className="flex-1 text-xs font-semibold text-gold-700">
              {t('booking.minOrder.hint', {
                minimum: formatCurrency(minGate.minOrderValue, minGate.currencyCode),
                shortfall: formatCurrency(minGate.shortfall, minGate.currencyCode),
              })}
            </Text>
          </View>
        ) : walletInsufficient ? (
          <View className="mb-3 flex-row items-center gap-2 rounded-xl bg-red-50 px-4 py-2.5">
            <Ionicons name="alert-circle-outline" size={16} color="#C0492F" />
            <Text className="flex-1 text-xs font-semibold text-danger">
              {t('booking.walletInsufficientAction')}
            </Text>
          </View>
        ) : null}
        <Pressable
          onPress={() => void handlePay()}
          disabled={loading || walletInsufficient}
          className={[
            'flex-row items-center justify-center gap-2 rounded-2xl py-4',
            loading ? 'bg-olive-400' : walletInsufficient || minBlocked ? 'bg-cream-300' : 'bg-olive-700',
          ].join(' ')}
          accessibilityLabel={
            minBlocked
              ? t('booking.minOrder.cta')
              : walletInsufficient
                ? t('booking.walletInsufficient')
                : `Pay ${rupees(total)}`
          }
          accessibilityState={{ disabled: loading || walletInsufficient }}
        >
          {loading ? <ActivityIndicator size="small" color="#FFFFFF" /> : null}
          <Text className={`text-base font-extrabold ${walletInsufficient || minBlocked ? 'text-ink-faint' : 'text-white'}`}>
            {loading
              ? t('booking.confirming')
              : minBlocked && minGate
                ? t('booking.minOrder.blockedCta', {
                    shortfall: formatCurrency(minGate.shortfall, minGate.currencyCode),
                  })
                : t('booking.pay', { amount: rupees(total) })}
          </Text>
          {!loading && !walletInsufficient && !minBlocked ? (
            <Ionicons name="arrow-forward" size={18} color="#FFFFFF" />
          ) : null}
        </Pressable>
      </View>

      {/* #23: blocking minimum-order sheet */}
      {minOrderPrompt ? (
        <MinOrderSheet
          visible
          currencyCode={minOrderPrompt.currencyCode}
          subtotal={minOrderPrompt.subtotal}
          minimum={minOrderPrompt.minimum}
          shortfall={minOrderPrompt.shortfall}
          onAddMore={handleAddMore}
          onClose={() => setMinOrderPrompt(null)}
        />
      ) : null}

      {/* GH #22: declared-value re-prompt after a value-slab 422 */}
      {declaredValuePrompt ? (
        <DeclaredValueSheet
          visible
          currencyCode={catalogConfig?.currencyCode ?? 'INR'}
          itemName={declaredValuePrompt.itemName}
          threshold={catalogConfig?.highValueGarmentThreshold ?? null}
          initialValue={declaredValuePrompt.initial}
          errorMessage={declaredValuePrompt.message}
          onSubmit={(value) => {
            setDeclaredValue(declaredValuePrompt.lineId, value);
            setDeclaredValuePrompt(null);
          }}
          onClose={() => setDeclaredValuePrompt(null)}
        />
      ) : null}
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
