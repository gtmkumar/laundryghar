/**
 * Declared-value bottom sheet (GitHub #22).
 *
 * Value-slab garments (branded / luxury items) are priced from a customer-declared
 * value, not a fixed rate. This sheet prompts for that value when the item is added
 * to the cart (and lets the customer change it later, or correct it after a server
 * rejection). The amount is validated to be > 0 and formatted from the currency code
 * (no hardcoded symbol). Matches the app's slide-up sheet pattern (see MinOrderSheet).
 */
import React, { useMemo, useState } from 'react';
import { Modal, Pressable, Text, TextInput, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import { formatCurrency } from '@/lib/minOrder';

interface DeclaredValueSheetProps {
  visible: boolean;
  /** ISO currency code for the input adornment / helper text. */
  currencyCode: string;
  /** Item name shown in the title, e.g. "Silk Saree". */
  itemName: string;
  /**
   * High-value threshold from catalog config, when set. Drives the helper copy
   * ("Branded/luxury items above ₹X are priced by declared value"). Null ⇒ generic copy.
   */
  threshold: number | null;
  /** Pre-fill for an edit / correction; undefined for a fresh add. */
  initialValue?: number;
  /** Optional server-side reason (e.g. a 422 retry) rendered as a banner. */
  errorMessage?: string;
  /** Commit a validated (> 0) value. */
  onSubmit: (value: number) => void;
  onClose: () => void;
}

export function DeclaredValueSheet({
  visible,
  currencyCode,
  itemName,
  threshold,
  initialValue,
  errorMessage,
  onSubmit,
  onClose,
}: DeclaredValueSheetProps) {
  const { t } = useTranslation();
  const [text, setText] = useState(initialValue != null ? String(initialValue) : '');
  const [touched, setTouched] = useState(false);

  // Reset local input whenever the sheet is (re)opened for a (different) item.
  React.useEffect(() => {
    if (visible) {
      setText(initialValue != null ? String(initialValue) : '');
      setTouched(false);
    }
  }, [visible, initialValue, itemName]);

  const parsed = useMemo(() => {
    const n = Number.parseFloat(text.replace(/[^0-9.]/g, ''));
    return Number.isFinite(n) ? n : NaN;
  }, [text]);
  const valid = Number.isFinite(parsed) && parsed > 0;

  const symbol = formatCurrency(0, currencyCode).replace(/[\d.,\s]/g, '') || currencyCode;

  const handleSubmit = () => {
    if (!valid) {
      setTouched(true);
      return;
    }
    onSubmit(Math.round(parsed));
  };

  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose}>
      <Pressable
        className="flex-1 justify-end bg-black/40"
        onPress={onClose}
        accessibilityLabel={t('common.close')}
      >
        <Pressable
          onPress={(e) => e.stopPropagation()}
          className="bg-cream px-5 pb-10 pt-5"
          style={{ borderTopLeftRadius: 28, borderTopRightRadius: 28 }}
          accessibilityViewIsModal
        >
          <View className="mb-4 h-1.5 w-12 self-center rounded-full bg-cream-300" />

          <View className="mb-3 h-14 w-14 items-center justify-center self-center rounded-full bg-gold-200">
            <Ionicons name="diamond-outline" size={26} color="#8A641D" />
          </View>

          <Text className="text-center text-lg font-extrabold text-ink">{itemName}</Text>
          <Text className="mt-1.5 text-center text-sm leading-5 text-ink-muted">
            {t('booking.valueSlab.question')}
          </Text>

          {errorMessage ? (
            <View className="mt-4 flex-row items-start gap-2 rounded-2xl bg-red-50 px-4 py-3">
              <Ionicons name="alert-circle-outline" size={16} color="#C0492F" style={{ marginTop: 1 }} />
              <Text className="flex-1 text-xs leading-4 text-danger">{errorMessage}</Text>
            </View>
          ) : null}

          {/* Currency input */}
          <View
            className={[
              'mt-5 flex-row items-center rounded-2xl border bg-white px-4 py-3',
              touched && !valid ? 'border-danger' : 'border-cream-300',
            ].join(' ')}
          >
            <Text className="mr-2 text-lg font-extrabold text-ink-muted">{symbol}</Text>
            <TextInput
              value={text}
              onChangeText={(v) => {
                setText(v);
                setTouched(true);
              }}
              onSubmitEditing={handleSubmit}
              keyboardType="number-pad"
              inputMode="numeric"
              returnKeyType="done"
              placeholder={t('booking.valueSlab.inputPlaceholder')}
              placeholderTextColor="#A8A493"
              autoFocus
              className="flex-1 text-lg font-extrabold text-ink"
              accessibilityLabel={t('booking.valueSlab.question')}
            />
          </View>

          {touched && !valid ? (
            <View className="mt-2 flex-row items-center gap-1.5">
              <Ionicons name="alert-circle-outline" size={14} color="#C0492F" />
              <Text className="text-xs text-danger">{t('booking.valueSlab.invalidValue')}</Text>
            </View>
          ) : null}

          {/* Helper: threshold-aware when configured */}
          <Text className="mt-3 text-xs leading-4 text-ink-muted">
            {threshold != null && threshold > 0
              ? t('booking.valueSlab.helperThreshold', {
                  threshold: formatCurrency(threshold, currencyCode),
                })
              : t('booking.valueSlab.helper')}
          </Text>
          <View className="mt-2 flex-row items-start gap-1.5">
            <Ionicons name="information-circle-outline" size={13} color="#8A641D" style={{ marginTop: 1 }} />
            <Text className="flex-1 text-xs leading-4 text-gold-700">
              {t('booking.valueSlab.note')}
            </Text>
          </View>

          <Pressable
            onPress={handleSubmit}
            disabled={!valid}
            accessibilityRole="button"
            accessibilityLabel={t('booking.valueSlab.save')}
            accessibilityState={{ disabled: !valid }}
            className={[
              'mt-5 flex-row items-center justify-center gap-2 rounded-2xl py-4',
              valid ? 'bg-olive-700' : 'bg-cream-300',
            ].join(' ')}
          >
            <Text className={`text-base font-extrabold ${valid ? 'text-white' : 'text-ink-faint'}`}>
              {t('booking.valueSlab.save')}
            </Text>
          </Pressable>
        </Pressable>
      </Pressable>
    </Modal>
  );
}
