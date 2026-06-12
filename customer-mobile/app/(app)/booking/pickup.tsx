/**
 * Booking step 2 — "Schedule pickup".
 * Address picker (from saved addresses), a day picker (next 7 days) and a live slot grid.
 *
 * MOB-1: slots now come from GET /api/v1/customer/delivery-slots?date=<iso>
 * storeId is not known pre-order (store is assigned server-side), so we omit it
 * and the endpoint returns all slots for the brand's active stores on that day.
 * Full slots (available=false) are rendered disabled and grayed.
 * The real UUID is stored so pay.tsx sends it instead of a stub id.
 */
import React, { useMemo, useState } from 'react';
import {
  ActivityIndicator,
  Modal,
  Pressable,
  ScrollView,
  Switch,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import { useAddresses } from '@/hooks/useCatalog';
import { useDeliverySlots } from '@/hooks/useOrders';
import { useBookingStore } from '@/store/bookingStore';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import type { CustomerAddressDto, DeliverySlotDto } from '@/types/api';

interface DayOption {
  iso: string;
  weekday: string;
  day: string;
  month: string;
}

function nextDays(n: number): DayOption[] {
  const out: DayOption[] = [];
  const base = new Date();
  for (let i = 0; i < n; i++) {
    const d = new Date(base);
    d.setDate(base.getDate() + i);
    out.push({
      iso: d.toISOString().slice(0, 10),
      weekday: d.toLocaleDateString('en-IN', { weekday: 'short' }).toUpperCase(),
      day: String(d.getDate()),
      month: d.toLocaleDateString('en-IN', { month: 'short' }),
    });
  }
  return out;
}

function formatSlotLabel(slot: DeliverySlotDto): string {
  // slotStart / slotEnd are time strings like "10:00:00"
  const fmt = (t: string) => {
    const [h, m] = t.split(':');
    const hour = parseInt(h, 10);
    const min = m === '00' ? '' : `:${m}`;
    const period = hour < 12 ? 'AM' : 'PM';
    const h12 = hour === 0 ? 12 : hour > 12 ? hour - 12 : hour;
    return `${h12}${min} ${period}`;
  };
  return `${fmt(slot.slotStart)} – ${fmt(slot.slotEnd)}`;
}

// ── Address picker modal ──────────────────────────────────────────────────────

function AddressPickerModal({
  visible,
  addresses,
  selectedId,
  onSelect,
  onClose,
  onAddNew,
}: {
  visible: boolean;
  addresses: CustomerAddressDto[];
  selectedId?: string;
  onSelect: (addr: CustomerAddressDto) => void;
  onClose: () => void;
  onAddNew: () => void;
}) {
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
          <Text className="text-lg font-extrabold text-ink">{t('booking.chooseAddress')}</Text>
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
            const icon =
              addr.label === 'home'
                ? 'home'
                : addr.label === 'work'
                  ? 'briefcase-outline'
                  : 'location-outline';
            const addressLines = [addr.addressLine1, addr.city, addr.pincode]
              .filter(Boolean)
              .join(', ');

            return (
              <Pressable
                key={addr.id}
                onPress={() => onSelect(addr)}
                className={[
                  'mb-3 flex-row items-start gap-3 rounded-2xl border p-4',
                  isSelected
                    ? 'border-olive-700 bg-olive-50'
                    : 'border-cream-300 bg-white',
                ].join(' ')}
                accessibilityRole="radio"
                accessibilityState={{ selected: isSelected }}
                accessibilityLabel={`${addr.label}: ${addressLines}`}
              >
                <View className="mt-0.5 h-9 w-9 items-center justify-center rounded-xl bg-olive-100">
                  <Ionicons name={icon} size={18} color="#5C6A33" />
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
                  <Text
                    className="mt-0.5 text-sm text-ink-muted"
                    numberOfLines={2}
                  >
                    {addressLines}
                  </Text>
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

// ── Slot grid ─────────────────────────────────────────────────────────────────

function SlotGrid({
  dayIso,
  selectedSlotId,
  onSelect,
}: {
  dayIso: string;
  selectedSlotId?: string;
  onSelect: (slot: DeliverySlotDto) => void;
}) {
  const { t } = useTranslation();
  // storeId is unknown pre-order; omit it — endpoint returns brand-level slots.
  // CUST-BUG-01: storeId is not needed — the endpoint accepts an optional storeId.
  // Do not render the error box when the query is idle/disabled (isError=false but
  // data is undefined). Only show the error UI when isError is explicitly true.
  const { data: slots, isLoading, isError } = useDeliverySlots(undefined, dayIso);

  if (isLoading) {
    return (
      <View className="mx-5 items-center py-8">
        <ActivityIndicator size="small" color="#5C6A33" />
      </View>
    );
  }

  if (isError) {
    return (
      <View className="mx-5 rounded-2xl bg-red-50 p-4">
        <Text className="text-xs text-red-600">{t('error.generic')}</Text>
      </View>
    );
  }

  // Filter to pickup slots only, sort by start time.
  // `slots` may be undefined on first mount before data arrives; treat as empty.
  const pickupSlots = (slots ?? [])
    .filter((s) => s.slotType === 'pickup' && s.isActive)
    .sort((a, b) => a.slotStart.localeCompare(b.slotStart));

  if (pickupSlots.length === 0) {
    return (
      <View className="mx-5 rounded-2xl bg-cream-100 p-4">
        <Text className="text-sm text-ink-muted">{t('booking.noSlotsForDay')}</Text>
      </View>
    );
  }

  return (
    <View className="mx-5 flex-row flex-wrap justify-between">
      {pickupSlots.map((s) => {
        const full = !s.available;
        const selected = selectedSlotId === s.id;
        const remaining = s.capacity - s.bookedCount;
        const fewLeft = remaining > 0 && remaining <= 3;

        const noteText = full
          ? t('booking.slotFull')
          : fewLeft
            ? t('booking.fewLeft')
            : s.isExpress
              ? t('booking.expressOnly')
              : t('booking.available');

        const noteColor = selected
          ? 'text-olive-100'
          : full
            ? 'text-danger'
            : fewLeft
              ? 'text-gold-600'
              : 'text-ink-muted';

        const label = formatSlotLabel(s);

        return (
          <Pressable
            key={s.id}
            disabled={full}
            onPress={() => onSelect(s)}
            accessibilityRole="radio"
            accessibilityState={{ selected, disabled: full }}
            accessibilityLabel={`${label} - ${noteText}`}
            className={[
              'mb-3 w-[48%] rounded-2xl border p-3.5',
              selected
                ? 'border-olive-700 bg-olive-700'
                : 'border-cream-300 bg-white',
              full ? 'opacity-50' : '',
            ].join(' ')}
          >
            <Text
              className={`text-base font-extrabold ${selected ? 'text-white' : 'text-ink'}`}
            >
              {label}
            </Text>
            <Text className={`mt-0.5 text-xs font-semibold ${noteColor}`}>
              {noteText}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}

// ── Screen ────────────────────────────────────────────────────────────────────

export default function PickupScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const { data: addresses, isLoading: addressesLoading } = useAddresses();
  const { address, setAddress, slot, setSlot, express, setExpress } =
    useBookingStore();

  // 7 days as required by MOB-1
  const days = useMemo(() => nextDays(7), []);
  const [dayIso, setDayIso] = useState(days[1]?.iso ?? days[0].iso);
  const [pickerVisible, setPickerVisible] = useState(false);

  const list = addresses ?? [];
  const defaultAddr = list.find((a) => a.isDefault) ?? list[0];

  // Seed the booking address once from the default address if not already set
  React.useEffect(() => {
    if (!address && defaultAddr) {
      setAddress({
        id: defaultAddr.id,
        label: defaultAddr.label ?? 'Home',
        line1: defaultAddr.addressLine1,
      });
    }
  }, [address, defaultAddr, setAddress]);

  const selectedAddr = address
    ? list.find((a) => a.id === address.id) ?? null
    : null;

  const addrText =
    selectedAddr
      ? `${selectedAddr.addressLine1}, ${selectedAddr.city}`
      : address?.line1 ?? 'Select a pickup address';

  const addrLabel = selectedAddr?.label ?? address?.label ?? 'Home';

  const canContinue = !!slot && !!address;

  const handleSelectAddress = (addr: CustomerAddressDto) => {
    setAddress({
      id: addr.id,
      label: addr.label ?? 'Home',
      line1: addr.addressLine1,
    });
    setPickerVisible(false);
  };

  const handleSelectSlot = (s: DeliverySlotDto) => {
    setSlot({
      id: s.id,        // real UUID from the backend
      date: dayIso,
      label: formatSlotLabel(s),
      windowStart: s.slotStart,
      windowEnd: s.slotEnd,
    });
  };

  const onContinue = () => {
    if (!slot) return;
    const chosenDay = days.find((d) => d.iso === dayIso);
    router.push({
      pathname: '/(app)/booking/pay',
      params: { dateLabel: `${chosenDay?.weekday} ${chosenDay?.day} ${chosenDay?.month}` },
    });
  };

  if (addressesLoading) return <ScreenLoader />;

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
        <Text className="text-xl font-extrabold text-ink">{t('booking.schedulePickup')}</Text>
      </View>

      <ScrollView
        showsVerticalScrollIndicator={false}
        contentContainerStyle={{ paddingBottom: 130 }}
      >
        {/* Address picker */}
        <Pressable
          onPress={() => setPickerVisible(true)}
          className="mx-5 flex-row items-center justify-between rounded-2xl bg-white p-4"
          accessibilityRole="button"
          accessibilityLabel={`${t('booking.pickupAt', { label: addrLabel })}: ${addrText}`}
        >
          <View className="flex-1 flex-row items-center gap-3">
            <View className="h-9 w-9 items-center justify-center rounded-xl bg-olive-100">
              <Ionicons name="home" size={18} color="#5C6A33" />
            </View>
            <View className="flex-1">
              <Text className="text-[11px] font-bold uppercase tracking-wider text-ink-faint">
                {t('booking.pickupAt', { label: addrLabel })}
              </Text>
              <Text
                className="text-sm font-bold text-ink"
                numberOfLines={1}
              >
                {addrText}
              </Text>
            </View>
          </View>
          <Text className="text-sm font-bold text-olive-700">{t('booking.change')}</Text>
        </Pressable>

        {/* No address warning */}
        {list.length === 0 ? (
          <Pressable
            onPress={() => router.push('/(app)/addresses' as never)}
            className="mx-5 mt-2 flex-row items-center gap-2 rounded-2xl bg-gold-100 p-3"
          >
            <Ionicons name="information-circle-outline" size={16} color="#8A641D" />
            <Text className="flex-1 text-xs font-semibold text-gold-800">
              {t('booking.noAddressWarning')}
            </Text>
            <Ionicons name="arrow-forward" size={14} color="#8A641D" />
          </Pressable>
        ) : null}

        {/* Pick a day — 7 days */}
        <Text className="mx-5 mb-3 mt-7 text-lg font-extrabold text-ink">
          {t('booking.pickADay')}
        </Text>
        <ScrollView
          horizontal
          showsHorizontalScrollIndicator={false}
          contentContainerStyle={{ paddingHorizontal: 20 }}
        >
          {days.map((d) => {
            const selected = d.iso === dayIso;
            return (
              <Pressable
                key={d.iso}
                onPress={() => {
                  setDayIso(d.iso);
                  // Clear slot when day changes to avoid stale slot UUID
                  setSlot(null);
                }}
                accessibilityRole="radio"
                accessibilityState={{ selected }}
                accessibilityLabel={`${d.weekday} ${d.day} ${d.month}`}
                className={`mr-3 w-[64px] items-center rounded-2xl py-3 ${selected ? 'bg-olive-700' : 'bg-white'}`}
              >
                <Text
                  className={`text-[11px] font-bold ${selected ? 'text-olive-100' : 'text-ink-faint'}`}
                >
                  {d.weekday}
                </Text>
                <Text
                  className={`my-0.5 text-xl font-extrabold ${selected ? 'text-white' : 'text-ink'}`}
                >
                  {d.day}
                </Text>
                <Text
                  className={`text-[11px] ${selected ? 'text-olive-100' : 'text-ink-muted'}`}
                >
                  {d.month}
                </Text>
              </Pressable>
            );
          })}
        </ScrollView>

        {/* Live slot grid */}
        <Text className="mx-5 mb-3 mt-7 text-lg font-extrabold text-ink">
          {t('booking.pickASlot')}
        </Text>
        <SlotGrid
          dayIso={dayIso}
          selectedSlotId={slot?.id}
          onSelect={handleSelectSlot}
        />

        {/* Express */}
        <View className="mx-5 mt-2 flex-row items-center justify-between rounded-2xl bg-gold-100 p-4">
          <View className="flex-1 flex-row items-center gap-3">
            <View className="h-9 w-9 items-center justify-center rounded-xl bg-gold-300">
              <Ionicons name="flash" size={18} color="#8A641D" />
            </View>
            <View>
              <Text className="text-sm font-bold text-ink">{t('booking.expressService')}</Text>
              <Text className="text-xs text-ink-muted">{t('booking.expressTurnaround')}</Text>
            </View>
          </View>
          <Switch
            value={express}
            onValueChange={setExpress}
            trackColor={{ true: '#73803F', false: '#D2C8B2' }}
            thumbColor="#FFFFFF"
            accessibilityRole="switch"
            accessibilityLabel={t('booking.expressService')}
            accessibilityState={{ checked: express }}
          />
        </View>
      </ScrollView>

      {/* Continue */}
      <View
        className="absolute inset-x-0 bottom-0 border-t border-cream-200 bg-white px-6 pb-8 pt-4"
        style={{ borderTopLeftRadius: 28, borderTopRightRadius: 28 }}
      >
        <Pressable
          disabled={!canContinue}
          onPress={onContinue}
          className={`flex-row items-center justify-center gap-2 rounded-2xl py-4 ${
            canContinue ? 'bg-gold-400' : 'bg-cream-300'
          }`}
          accessibilityRole="button"
          accessibilityLabel={t('booking.continueToPayment')}
          accessibilityState={{ disabled: !canContinue }}
        >
          <Text
            className={`text-base font-extrabold ${
              canContinue ? 'text-olive-900' : 'text-ink-faint'
            }`}
          >
            {t('booking.continueToPayment')}
          </Text>
          <Ionicons
            name="arrow-forward"
            size={18}
            color={canContinue ? '#2E351C' : '#A8A493'}
          />
        </Pressable>
      </View>

      {/* Address picker modal */}
      <AddressPickerModal
        visible={pickerVisible}
        addresses={list}
        selectedId={address?.id}
        onSelect={handleSelectAddress}
        onClose={() => setPickerVisible(false)}
        onAddNew={() => {
          setPickerVisible(false);
          router.push('/(app)/addresses' as never);
        }}
      />
    </SafeAreaView>
  );
}
