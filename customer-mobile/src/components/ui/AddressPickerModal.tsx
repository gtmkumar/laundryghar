/**
 * Reusable saved-address picker modal.
 *
 * Extracted from the laundry booking pickup screen so the parcel flow can reuse
 * the exact same look: a slide-up page sheet listing the customer's saved
 * addresses; the selected row gets an olive border + checkmark; a dashed
 * "Add new address" row routes to /(app)/addresses.
 *
 * `disabledId` greys out and blocks selection of a single address — used by the
 * parcel drop screen so the drop cannot equal the already-chosen pickup.
 */
import React from 'react';
import { Modal, Pressable, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import type { CustomerAddressDto } from '@/types/api';

interface AddressPickerModalProps {
  visible: boolean;
  title: string;
  addresses: CustomerAddressDto[];
  selectedId?: string;
  /** Address that cannot be picked here (greyed + disabled), e.g. the pickup on the drop screen. */
  disabledId?: string;
  /** Helper line shown on a disabled row, e.g. "Already the pickup address". */
  disabledNote?: string;
  onSelect: (addr: CustomerAddressDto) => void;
  onClose: () => void;
  onAddNew: () => void;
}

function addrIcon(label: string): React.ComponentProps<typeof Ionicons>['name'] {
  if (label === 'home') return 'home';
  if (label === 'work') return 'briefcase-outline';
  return 'location-outline';
}

export function AddressPickerModal({
  visible,
  title,
  addresses,
  selectedId,
  disabledId,
  disabledNote,
  onSelect,
  onClose,
  onAddNew,
}: AddressPickerModalProps) {
  const { t } = useTranslation();
  return (
    <Modal
      visible={visible}
      animationType="slide"
      presentationStyle="pageSheet"
      onRequestClose={onClose}
    >
      <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
        <View className="flex-row items-center justify-between border-b border-cream-200 px-5 pb-4 pt-5">
          <Text className="text-lg font-extrabold text-ink">{title}</Text>
          <Pressable
            onPress={onClose}
            className="h-9 w-9 items-center justify-center rounded-full bg-cream-200"
            accessibilityLabel={t('common.close')}
          >
            <Ionicons name="close" size={20} color="#3C3F35" />
          </Pressable>
        </View>

        <ScrollView
          contentContainerStyle={{ padding: 20, paddingBottom: 40 }}
          showsVerticalScrollIndicator={false}
        >
          {addresses.map((addr) => {
            const isSelected = addr.id === selectedId;
            const isDisabled = addr.id === disabledId;
            const addressLines = [addr.addressLine1, addr.city, addr.pincode]
              .filter(Boolean)
              .join(', ');

            return (
              <Pressable
                key={addr.id}
                disabled={isDisabled}
                onPress={() => onSelect(addr)}
                className={[
                  'mb-3 flex-row items-start gap-3 rounded-2xl border p-4',
                  isSelected
                    ? 'border-olive-700 bg-olive-50'
                    : 'border-cream-300 bg-white',
                  isDisabled ? 'opacity-50' : '',
                ].join(' ')}
                accessibilityRole="radio"
                accessibilityState={{ selected: isSelected, disabled: isDisabled }}
                accessibilityLabel={`${addr.label}: ${addressLines}`}
              >
                <View className="mt-0.5 h-9 w-9 items-center justify-center rounded-xl bg-olive-100">
                  <Ionicons name={addrIcon(addr.label)} size={18} color="#5C6A33" />
                </View>
                <View className="flex-1">
                  <View className="flex-row items-center gap-2">
                    <Text className="text-sm font-extrabold capitalize text-ink">
                      {addr.label}
                    </Text>
                    {addr.isDefault ? (
                      <View className="rounded-full bg-gold-200 px-2 py-0.5">
                        <Text className="text-[10px] font-bold text-gold-800">
                          {t('common.default')}
                        </Text>
                      </View>
                    ) : null}
                  </View>
                  <Text className="mt-0.5 text-sm text-ink-muted" numberOfLines={2}>
                    {addressLines}
                  </Text>
                  {isDisabled && disabledNote ? (
                    <Text className="mt-1 text-xs font-semibold text-gold-700">
                      {disabledNote}
                    </Text>
                  ) : null}
                </View>
                {isSelected ? (
                  <Ionicons
                    name="checkmark-circle"
                    size={20}
                    color="#73803F"
                    style={{ marginTop: 4 }}
                  />
                ) : null}
              </Pressable>
            );
          })}

          <Pressable
            onPress={onAddNew}
            className="flex-row items-center gap-3 rounded-2xl border border-dashed border-olive-400 p-4"
            accessibilityRole="button"
            accessibilityLabel={t('booking.addNewAddress')}
          >
            <View className="h-9 w-9 items-center justify-center rounded-xl bg-olive-100">
              <Ionicons name="add" size={20} color="#5C6A33" />
            </View>
            <Text className="text-sm font-bold text-olive-700">
              {t('booking.addNewAddress')}
            </Text>
          </Pressable>
        </ScrollView>
      </SafeAreaView>
    </Modal>
  );
}
