/**
 * Booking step 2 — "Schedule pickup".
 * Address (from saved addresses), a day picker (next 5 days) and a slot grid.
 * Selection is held in the booking store. Express toggle adds a surcharge at pay.
 *
 * Slots are presented as a fixed daily grid; live capacity wiring
 * (GET {Orders}/customer/delivery-slots) needs a resolved store, which this
 * pre-order flow doesn't have yet — so availability here is illustrative.
 */
import React, { useMemo, useState } from 'react';
import { Pressable, ScrollView, Switch, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useAddresses } from '@/hooks/useCatalog';
import { useBookingStore } from '@/store/bookingStore';

interface DayOption {
  iso: string;
  weekday: string;
  day: string;
  month: string;
}

interface SlotOption {
  id: string;
  label: string;
  note: string;
  status: 'available' | 'few' | 'full' | 'express';
}

const SLOTS: SlotOption[] = [
  { id: 's-10', label: '10 – 12 AM', note: 'Few left',     status: 'few' },
  { id: 's-12', label: '12 – 2 PM',  note: 'Pradeep K.',   status: 'available' },
  { id: 's-14', label: '2 – 4 PM',   note: 'Slot full',    status: 'full' },
  { id: 's-16', label: '4 – 6 PM',   note: 'Available',    status: 'available' },
  { id: 's-18', label: '6 – 8 PM',   note: 'Express only', status: 'express' },
  { id: 's-20', label: '8 – 10 PM',  note: 'Slot full',    status: 'full' },
];

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

export default function PickupScreen() {
  const router = useRouter();
  const { data: addresses } = useAddresses();
  const { address, setAddress, slot, setSlot, express, setExpress } = useBookingStore();

  const days = useMemo(() => nextDays(5), []);
  const [dayIso, setDayIso] = useState(days[1]?.iso ?? days[0].iso);

  const defaultAddr = addresses?.find((a) => a.isDefault) ?? addresses?.[0];
  const addrText = address?.line1
    ?? (defaultAddr ? `${defaultAddr.line1}${defaultAddr.label ? `, ${defaultAddr.label}` : ''}` : 'H-204, DLF Phase 4');

  // Seed the booking address once if not set.
  React.useEffect(() => {
    if (!address && defaultAddr) {
      setAddress({ id: defaultAddr.id, label: defaultAddr.label ?? 'Home', line1: defaultAddr.line1 });
    }
  }, [address, defaultAddr, setAddress]);

  const canContinue = !!slot;

  const onContinue = () => {
    if (!slot) return;
    const chosenDay = days.find((d) => d.iso === dayIso);
    setSlot({
      id: slot.id,
      date: dayIso,
      label: slot.label,
    });
    router.push({
      pathname: '/(app)/booking/pay',
      params: { dateLabel: `${chosenDay?.weekday} ${chosenDay?.day} ${chosenDay?.month}` },
    });
  };

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      {/* Header */}
      <View className="flex-row items-center gap-3 px-5 pt-2 pb-3">
        <Pressable
          onPress={() => router.back()}
          className="h-10 w-10 items-center justify-center rounded-full bg-white"
          accessibilityLabel="Go back"
        >
          <Ionicons name="chevron-back" size={22} color="#3C3F35" />
        </Pressable>
        <Text className="text-xl font-extrabold text-ink">Schedule pickup</Text>
      </View>

      <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 130 }}>
        {/* Address */}
        <View className="mx-5 flex-row items-center justify-between rounded-2xl bg-white p-4">
          <View className="flex-1 flex-row items-center gap-3">
            <View className="h-9 w-9 items-center justify-center rounded-xl bg-olive-100">
              <Ionicons name="home" size={18} color="#5C6A33" />
            </View>
            <View className="flex-1">
              <Text className="text-[11px] font-bold uppercase tracking-wider text-ink-faint">Pickup at</Text>
              <Text className="text-sm font-bold text-ink" numberOfLines={1}>{addrText}</Text>
            </View>
          </View>
          <Pressable hitSlop={6}>
            <Text className="text-sm font-bold text-olive-700">Change</Text>
          </Pressable>
        </View>

        {/* Pick a day */}
        <Text className="mx-5 mb-3 mt-7 text-lg font-extrabold text-ink">Pick a day</Text>
        <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={{ paddingHorizontal: 20 }}>
          {days.map((d) => {
            const selected = d.iso === dayIso;
            return (
              <Pressable
                key={d.iso}
                onPress={() => setDayIso(d.iso)}
                className={`mr-3 w-[64px] items-center rounded-2xl py-3 ${selected ? 'bg-olive-700' : 'bg-white'}`}
              >
                <Text className={`text-[11px] font-bold ${selected ? 'text-olive-100' : 'text-ink-faint'}`}>{d.weekday}</Text>
                <Text className={`my-0.5 text-xl font-extrabold ${selected ? 'text-white' : 'text-ink'}`}>{d.day}</Text>
                <Text className={`text-[11px] ${selected ? 'text-olive-100' : 'text-ink-muted'}`}>{d.month}</Text>
              </Pressable>
            );
          })}
        </ScrollView>

        {/* Pick a slot */}
        <Text className="mx-5 mb-3 mt-7 text-lg font-extrabold text-ink">Pick a slot</Text>
        <View className="mx-5 flex-row flex-wrap justify-between">
          {SLOTS.map((s) => {
            const disabled = s.status === 'full';
            const selected = slot?.id === s.id;
            const noteColor = selected
              ? 'text-olive-100'
              : s.status === 'full'
                ? 'text-danger'
                : s.status === 'express'
                  ? 'text-gold-600'
                  : 'text-ink-muted';
            return (
              <Pressable
                key={s.id}
                disabled={disabled}
                onPress={() => setSlot({ id: s.id, date: dayIso, label: s.label })}
                className={[
                  'mb-3 w-[48%] rounded-2xl border p-3.5',
                  selected ? 'border-olive-700 bg-olive-700' : 'border-cream-300 bg-white',
                  disabled ? 'opacity-50' : '',
                ].join(' ')}
              >
                <Text className={`text-base font-extrabold ${selected ? 'text-white' : 'text-ink'}`}>{s.label}</Text>
                <Text className={`mt-0.5 text-xs font-semibold ${noteColor}`}>{s.note}</Text>
              </Pressable>
            );
          })}
        </View>

        {/* Express */}
        <View className="mx-5 mt-2 flex-row items-center justify-between rounded-2xl bg-gold-100 p-4">
          <View className="flex-1 flex-row items-center gap-3">
            <View className="h-9 w-9 items-center justify-center rounded-xl bg-gold-300">
              <Ionicons name="flash" size={18} color="#8A641D" />
            </View>
            <View>
              <Text className="text-sm font-bold text-ink">Express service</Text>
              <Text className="text-xs text-ink-muted">4-hr turnaround · +₹50</Text>
            </View>
          </View>
          <Switch
            value={express}
            onValueChange={setExpress}
            trackColor={{ true: '#73803F', false: '#D2C8B2' }}
            thumbColor="#FFFFFF"
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
          className={`flex-row items-center justify-center gap-2 rounded-2xl py-4 ${canContinue ? 'bg-gold-400' : 'bg-cream-300'}`}
          accessibilityLabel="Continue to payment"
        >
          <Text className={`text-base font-extrabold ${canContinue ? 'text-olive-900' : 'text-ink-faint'}`}>
            Continue – review &amp; pay
          </Text>
          <Ionicons name="arrow-forward" size={18} color={canContinue ? '#2E351C' : '#A8A493'} />
        </Pressable>
      </View>
    </SafeAreaView>
  );
}
