/**
 * Saved Addresses screen — list / add / edit / delete / set-default.
 * GET  {Catalog}/api/v1/customer/addresses
 * POST {Catalog}/api/v1/customer/addresses
 * PUT  {Catalog}/api/v1/customer/addresses/{id}
 * DEL  {Catalog}/api/v1/customer/addresses/{id}
 */
import React, { useCallback, useEffect, useState } from 'react';
import {
  Alert,
  Modal,
  Pressable,
  ScrollView,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import {
  useAddresses,
  useCreateAddress,
  useDeleteAddress,
  useUpdateAddress,
} from '@/hooks/useCatalog';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { Button } from '@/components/ui/Button';
import { TextInput } from '@/components/ui/TextInput';
import type { CreateAddressRequest, CustomerAddressDto } from '@/types/api';

// ── Address form ──────────────────────────────────────────────────────────────

const LABEL_OPTIONS = ['home', 'work', 'other'] as const;
type LabelPreset = (typeof LABEL_OPTIONS)[number];

interface FormState {
  label: LabelPreset;
  addressLine1: string;
  addressLine2: string;
  landmark: string;
  city: string;
  state: string;
  pincode: string;
  isDefault: boolean;
}

const EMPTY_FORM: FormState = {
  label: 'home',
  addressLine1: '',
  addressLine2: '',
  landmark: '',
  city: '',
  state: '',
  pincode: '',
  isDefault: false,
};

function fromDto(dto: CustomerAddressDto): FormState {
  return {
    label: (LABEL_OPTIONS as readonly string[]).includes(dto.label)
      ? (dto.label as LabelPreset)
      : 'other',
    addressLine1: dto.addressLine1,
    addressLine2: dto.addressLine2 ?? '',
    landmark: dto.landmark ?? '',
    city: dto.city,
    state: dto.state,
    pincode: dto.pincode,
    isDefault: dto.isDefault,
  };
}

function validate(f: FormState): string | null {
  if (!f.addressLine1.trim()) return 'addresses.validation.addressLine1Required';
  if (!f.city.trim()) return 'addresses.validation.cityRequired';
  if (!f.state.trim()) return 'addresses.validation.stateRequired';
  if (!/^\d{6}$/.test(f.pincode)) return 'addresses.validation.pincodeInvalid';
  return null;
}

interface AddressFormProps {
  initialValues: FormState;
  onSave: (values: CreateAddressRequest) => void;
  onCancel: () => void;
  saving: boolean;
  title: string;
}

function AddressForm({
  initialValues,
  onSave,
  onCancel,
  saving,
  title,
}: AddressFormProps) {
  const { t } = useTranslation();
  const [form, setForm] = useState<FormState>(initialValues);
  const [error, setError] = useState<string | null>(null);

  const set = useCallback(
    (key: keyof FormState) => (val: string | boolean) =>
      setForm((prev) => ({ ...prev, [key]: val })),
    [],
  );

  const handleSave = () => {
    const err = validate(form);
    if (err) {
      setError(err);
      return;
    }
    setError(null);
    onSave({
      label: form.label,
      addressLine1: form.addressLine1.trim(),
      addressLine2: form.addressLine2.trim() || undefined,
      landmark: form.landmark.trim() || undefined,
      city: form.city.trim(),
      state: form.state.trim(),
      pincode: form.pincode.trim(),
      countryCode: 'IN',
      isDefault: form.isDefault,
    });
  };

  return (
    <View className="flex-1 bg-cream">
      {/* Sheet header */}
      <View
        className="flex-row items-center justify-between border-b border-cream-200 px-5 pb-4 pt-5"
        style={{ backgroundColor: '#FEFAF4' }}
      >
        <Text className="text-lg font-extrabold text-ink">{title}</Text>
        <Pressable
          onPress={onCancel}
          accessibilityRole="button"
          className="h-9 w-9 items-center justify-center rounded-full bg-cream-200"
          accessibilityLabel={t('a11y.closeModal')}
        >
          <Ionicons name="close" size={20} color="#3C3F35" />
        </Pressable>
      </View>

      <ScrollView
        contentContainerStyle={{ padding: 20, paddingBottom: 120 }}
        showsVerticalScrollIndicator={false}
        keyboardShouldPersistTaps="handled"
      >
        {/* Label presets */}
        <Text className="mb-2 text-xs font-bold uppercase tracking-wider text-ink-muted">
          {t('addresses.labelPreset')}
        </Text>
        <View className="mb-5 flex-row gap-3">
          {LABEL_OPTIONS.map((opt) => (
            <Pressable
              key={opt}
              onPress={() => set('label')(opt)}
              className={[
                'flex-1 items-center rounded-2xl border py-3',
                form.label === opt
                  ? 'border-olive-700 bg-olive-700'
                  : 'border-cream-300 bg-white',
              ].join(' ')}
              accessibilityLabel={opt}
              accessibilityRole="radio"
              accessibilityState={{ selected: form.label === opt }}
            >
              <Ionicons
                name={
                  opt === 'home'
                    ? 'home'
                    : opt === 'work'
                      ? 'briefcase'
                      : 'location'
                }
                size={18}
                color={form.label === opt ? '#FFFFFF' : '#5C6A33'}
              />
              <Text
                className={`mt-1 text-xs font-bold capitalize ${form.label === opt ? 'text-white' : 'text-ink'}`}
              >
                {opt}
              </Text>
            </Pressable>
          ))}
        </View>

        <View className="gap-4">
          <TextInput
            label={t('addresses.addressLine1')}
            placeholder={t('addresses.addressLine1Placeholder')}
            value={form.addressLine1}
            onChangeText={set('addressLine1')}
            autoCapitalize="words"
            returnKeyType="next"
          />
          <TextInput
            label={`${t('addresses.addressLine2')} ${t('common.optional')}`}
            placeholder={t('addresses.addressLine2Placeholder')}
            value={form.addressLine2}
            onChangeText={set('addressLine2')}
            autoCapitalize="words"
            returnKeyType="next"
          />
          <TextInput
            label={`${t('addresses.landmark')} ${t('common.optional')}`}
            placeholder={t('addresses.landmarkPlaceholder')}
            value={form.landmark}
            onChangeText={set('landmark')}
            autoCapitalize="words"
            returnKeyType="next"
          />
          <View className="flex-row gap-3">
            <View className="flex-1">
              <TextInput
                label={t('addresses.city')}
                placeholder={t('addresses.cityPlaceholder')}
                value={form.city}
                onChangeText={set('city')}
                autoCapitalize="words"
                returnKeyType="next"
              />
            </View>
            <View className="flex-1">
              <TextInput
                label={t('addresses.state')}
                placeholder={t('addresses.statePlaceholder')}
                value={form.state}
                onChangeText={set('state')}
                autoCapitalize="words"
                returnKeyType="next"
              />
            </View>
          </View>
          <TextInput
            label={t('addresses.pincode')}
            placeholder={t('addresses.pincodePlaceholder')}
            value={form.pincode}
            onChangeText={set('pincode')}
            keyboardType="numeric"
            maxLength={6}
            returnKeyType="done"
          />
        </View>

        {/* Set as default */}
        <Pressable
          onPress={() => set('isDefault')(!form.isDefault)}
          className="mt-5 flex-row items-center gap-3 rounded-2xl bg-white p-4"
          accessibilityRole="checkbox"
          accessibilityState={{ checked: form.isDefault }}
          accessibilityLabel="Set as default address"
        >
          <View
            className={`h-6 w-6 items-center justify-center rounded-lg border-2 ${
              form.isDefault
                ? 'border-olive-700 bg-olive-700'
                : 'border-cream-300 bg-white'
            }`}
          >
            {form.isDefault ? (
              <Ionicons name="checkmark" size={14} color="#FFFFFF" />
            ) : null}
          </View>
          <Text className="flex-1 text-sm font-semibold text-ink">
            {t('addresses.setAsDefault')}
          </Text>
        </Pressable>

        {error ? (
          <Text className="mt-3 text-sm text-danger" accessibilityRole="alert">
            {t(error)}
          </Text>
        ) : null}
      </ScrollView>

      {/* Save button */}
      <View
        className="absolute inset-x-0 bottom-0 border-t border-cream-200 bg-white px-6 pb-8 pt-4"
        style={{ borderTopLeftRadius: 28, borderTopRightRadius: 28 }}
      >
        <Button
          title={saving ? t('profile.saving') : t('addresses.saveAddress')}
          fullWidth
          loading={saving}
          onPress={handleSave}
          accessibilityLabel={t('addresses.saveAddress')}
        />
      </View>
    </View>
  );
}

// ── Address card ──────────────────────────────────────────────────────────────

function AddressCard({
  address,
  onEdit,
  onDelete,
  onSetDefault,
}: {
  address: CustomerAddressDto;
  onEdit: () => void;
  onDelete: () => void;
  onSetDefault: () => void;
}) {
  const { t } = useTranslation();
  const lines = [
    address.addressLine1,
    address.addressLine2,
    address.landmark ? t('addresses.nearLandmark', { landmark: address.landmark }) : null,
    `${address.city}, ${address.state} – ${address.pincode}`,
  ]
    .filter(Boolean)
    .join(', ');

  const icon =
    address.label === 'home'
      ? 'home'
      : address.label === 'work'
        ? 'briefcase-outline'
        : 'location-outline';

  return (
    <View
      className="mb-3 rounded-3xl bg-white px-4 py-4"
      style={{
        shadowColor: '#2E351C',
        shadowOpacity: 0.04,
        shadowRadius: 8,
        shadowOffset: { width: 0, height: 2 },
        elevation: 1,
      }}
    >
      <View className="flex-row items-start gap-3">
        <View className="mt-0.5 h-9 w-9 items-center justify-center rounded-xl bg-olive-100">
          <Ionicons name={icon} size={18} color="#5C6A33" />
        </View>
        <View className="flex-1">
          <View className="flex-row items-center gap-2">
            <Text className="text-base font-extrabold capitalize text-ink">
              {address.label}
            </Text>
            {address.isDefault ? (
              <View className="rounded-full bg-gold-200 px-2 py-0.5">
                <Text className="text-[10px] font-bold text-gold-800">
                  {t('common.default')}
                </Text>
              </View>
            ) : null}
          </View>
          <Text
            className="mt-0.5 text-sm leading-5 text-ink-muted"
            numberOfLines={2}
          >
            {lines}
          </Text>
        </View>
      </View>

      <View className="mt-3 flex-row gap-2 border-t border-cream-200 pt-3">
        <Pressable
          onPress={onEdit}
          className="flex-row items-center gap-1 rounded-xl bg-cream-100 px-3 py-2"
          accessibilityLabel={t('common.edit')}
          accessibilityRole="button"
        >
          <Ionicons name="pencil-outline" size={14} color="#5C6A33" />
          <Text className="text-xs font-bold text-olive-700">{t('common.edit')}</Text>
        </Pressable>
        {!address.isDefault ? (
          <Pressable
            onPress={onSetDefault}
            className="flex-row items-center gap-1 rounded-xl bg-cream-100 px-3 py-2"
            accessibilityLabel={t('addresses.setDefault')}
            accessibilityRole="button"
          >
            <Ionicons name="star-outline" size={14} color="#5C6A33" />
            <Text className="text-xs font-bold text-olive-700">{t('addresses.setDefault')}</Text>
          </Pressable>
        ) : null}
        <Pressable
          onPress={onDelete}
          className="ml-auto flex-row items-center gap-1 rounded-xl bg-red-50 px-3 py-2"
          accessibilityLabel={t('common.delete')}
          accessibilityRole="button"
        >
          <Ionicons name="trash-outline" size={14} color="#C0492F" />
          <Text className="text-xs font-bold text-danger">{t('common.delete')}</Text>
        </Pressable>
      </View>
    </View>
  );
}

// ── Screen ────────────────────────────────────────────────────────────────────

export default function AddressesScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const { data: addresses, isLoading } = useAddresses();
  const createMutation = useCreateAddress();
  const updateMutation = useUpdateAddress();
  const deleteMutation = useDeleteAddress();

  const [modalVisible, setModalVisible] = useState(false);
  const [editingAddress, setEditingAddress] = useState<CustomerAddressDto | null>(null);

  const isSaving = createMutation.isPending || updateMutation.isPending;

  const openAdd = () => {
    setEditingAddress(null);
    setModalVisible(true);
  };

  const openEdit = (addr: CustomerAddressDto) => {
    setEditingAddress(addr);
    setModalVisible(true);
  };

  const closeModal = () => {
    setModalVisible(false);
    setEditingAddress(null);
  };

  const handleSave = (values: CreateAddressRequest) => {
    if (editingAddress) {
      updateMutation.mutate(
        { id: editingAddress.id, body: values },
        {
          onSuccess: closeModal,
          onError: (err) =>
            Alert.alert(t('error.generic'), err instanceof Error ? err.message : t('error.tryAgain')),
        },
      );
    } else {
      createMutation.mutate(values, {
        onSuccess: closeModal,
        onError: (err) =>
          Alert.alert(t('error.generic'), err instanceof Error ? err.message : t('error.tryAgain')),
      });
    }
  };

  const handleDelete = (addr: CustomerAddressDto) => {
    Alert.alert(t('addresses.deleteConfirm'), t('addresses.deleteMessage', { label: addr.label }), [
      { text: t('common.cancel'), style: 'cancel' },
      {
        text: t('common.delete'),
        style: 'destructive',
        onPress: () =>
          deleteMutation.mutate(addr.id, {
            onError: () =>
              Alert.alert(t('error.generic'), t('error.tryAgain')),
          }),
      },
    ]);
  };

  const handleSetDefault = (addr: CustomerAddressDto) => {
    updateMutation.mutate({
      id: addr.id,
      body: {
        label: addr.label as 'home' | 'work' | 'other',
        addressLine1: addr.addressLine1,
        addressLine2: addr.addressLine2,
        landmark: addr.landmark,
        city: addr.city,
        state: addr.state,
        pincode: addr.pincode,
        countryCode: addr.countryCode,
        isDefault: true,
      },
    });
  };

  if (isLoading) return <ScreenLoader />;

  const list = addresses ?? [];

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      {/* Header */}
      <View className="flex-row items-center gap-3 px-5 pb-2 pt-2">
        <Pressable
          onPress={() => router.back()}
          className="h-10 w-10 items-center justify-center rounded-full bg-white"
          accessibilityRole="button"
          accessibilityLabel={t('a11y.back')}
        >
          <Ionicons name="chevron-back" size={22} color="#3C3F35" />
        </Pressable>
        <Text className="flex-1 text-xl font-extrabold text-ink">
          {t('addresses.title')}
        </Text>
        <Pressable
          onPress={openAdd}
          className="h-10 w-10 items-center justify-center rounded-full bg-gold-400"
          accessibilityLabel={t('a11y.addAddress')}
          accessibilityRole="button"
        >
          <Ionicons name="add" size={22} color="#2E351C" />
        </Pressable>
      </View>

      <ScrollView
        contentContainerStyle={{ padding: 20, paddingBottom: 40 }}
        showsVerticalScrollIndicator={false}
      >
        {list.length === 0 ? (
          <View className="mt-16 items-center gap-3">
            <View className="h-16 w-16 items-center justify-center rounded-full bg-olive-100">
              <Ionicons name="location-outline" size={36} color="#5C6A33" />
            </View>
            <Text className="text-lg font-extrabold text-ink">
              {t('addresses.noAddresses')}
            </Text>
            <Text className="text-center text-sm text-ink-muted">
              {t('addresses.noAddressesMessage')}
            </Text>
            <Button
              title={t('addresses.addAddress')}
              onPress={openAdd}
              iconLeft="add"
              variant="primary"
            />
          </View>
        ) : (
          list.map((addr) => (
            <AddressCard
              key={addr.id}
              address={addr}
              onEdit={() => openEdit(addr)}
              onDelete={() => handleDelete(addr)}
              onSetDefault={() => handleSetDefault(addr)}
            />
          ))
        )}
      </ScrollView>

      {/* Add / Edit modal */}
      <Modal
        visible={modalVisible}
        animationType="slide"
        presentationStyle="pageSheet"
        onRequestClose={closeModal}
      >
        <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
          <AddressForm
            key={editingAddress?.id ?? 'new'}
            title={editingAddress ? t('addresses.editAddress') : t('addresses.addAddress')}
            initialValues={
              editingAddress ? fromDto(editingAddress) : EMPTY_FORM
            }
            onSave={handleSave}
            onCancel={closeModal}
            saving={isSaving}
          />
        </SafeAreaView>
      </Modal>
    </SafeAreaView>
  );
}
