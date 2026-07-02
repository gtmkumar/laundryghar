/**
 * Minimum-order bottom sheet (GitHub #23).
 *
 * Blocking sheet shown when the customer tries to place an order whose ITEM
 * subtotal is below the store's configured `minOrderValue`. Presents the current
 * total, the required minimum, and the shortfall — all formatted from the
 * currency code (no hardcoded symbol) — with a single CTA that returns to the
 * item picker to add more. Matches the app's slide-up sheet pattern.
 */
import React from 'react';
import { Modal, Pressable, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import { formatCurrency } from '@/lib/minOrder';

interface MinOrderSheetProps {
  visible: boolean;
  currencyCode: string;
  /** Current item subtotal. */
  subtotal: number;
  /** Required minimum order value. */
  minimum: number;
  /** How much more the customer must add. */
  shortfall: number;
  /** CTA — send the user back to add more items. */
  onAddMore: () => void;
  onClose: () => void;
}

export function MinOrderSheet({
  visible,
  currencyCode,
  subtotal,
  minimum,
  shortfall,
  onAddMore,
  onClose,
}: MinOrderSheetProps) {
  const { t } = useTranslation();
  const shortfallLabel = formatCurrency(shortfall, currencyCode);

  return (
    <Modal
      visible={visible}
      transparent
      animationType="fade"
      onRequestClose={onClose}
    >
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
            <Ionicons name="cart-outline" size={28} color="#8A641D" />
          </View>

          <Text className="text-center text-lg font-extrabold text-ink">
            {t('booking.minOrder.title')}
          </Text>
          <Text className="mt-1.5 text-center text-sm leading-5 text-ink-muted">
            {t('booking.minOrder.message', { shortfall: shortfallLabel })}
          </Text>

          {/* Total vs minimum breakdown */}
          <View className="mt-5 rounded-2xl bg-white p-4">
            <View className="flex-row items-center justify-between py-1">
              <Text className="text-sm text-ink-muted">{t('booking.minOrder.yourTotal')}</Text>
              <Text className="text-sm font-bold text-ink-soft">
                {formatCurrency(subtotal, currencyCode)}
              </Text>
            </View>
            <View className="flex-row items-center justify-between py-1">
              <Text className="text-sm text-ink-muted">{t('booking.minOrder.minimum')}</Text>
              <Text className="text-sm font-bold text-ink-soft">
                {formatCurrency(minimum, currencyCode)}
              </Text>
            </View>
            <View className="mt-2 flex-row items-center justify-between border-t border-cream-200 pt-3">
              <Text className="text-sm font-extrabold text-ink">{t('booking.minOrder.shortfall')}</Text>
              <Text className="text-sm font-extrabold text-danger">{shortfallLabel}</Text>
            </View>
          </View>

          <Pressable
            onPress={onAddMore}
            accessibilityRole="button"
            accessibilityLabel={t('booking.minOrder.cta')}
            className="mt-5 flex-row items-center justify-center gap-2 rounded-2xl bg-olive-700 py-4"
          >
            <Ionicons name="add" size={20} color="#FFFFFF" />
            <Text className="text-base font-extrabold text-white">
              {t('booking.minOrder.cta')}
            </Text>
          </Pressable>
        </Pressable>
      </Pressable>
    </Modal>
  );
}
