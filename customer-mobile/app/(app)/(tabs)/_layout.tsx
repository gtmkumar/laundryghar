/**
 * Bottom tab bar with a central "schedule pickup" FAB.
 * Tabs: Home · Orders · [ + ] · Wallet · Profile.
 * The FAB launches the booking flow (items picker).
 */
import React, { useState } from 'react';
import { Modal, Pressable, Text, View } from 'react-native';
import { Tabs, useRouter } from 'expo-router';
import type { BottomTabBarProps } from '@react-navigation/bottom-tabs';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import { useBookingStore } from '@/store/bookingStore';

type IoniconName = React.ComponentProps<typeof Ionicons>['name'];

interface TabMeta {
  name: string;
  labelKey: string;
  a11yKey: string;
  icon: IoniconName;
  activeIcon: IoniconName;
}

// Order matters — the FAB is injected between index 1 and 2.
const TAB_META: Record<string, TabMeta> = {
  home:        { name: 'home',      labelKey: 'home.tabLabel',     a11yKey: 'a11y.tabHome',    icon: 'home-outline',    activeIcon: 'home' },
  'my-orders': { name: 'my-orders', labelKey: 'orders.tabLabel',   a11yKey: 'a11y.tabOrders',  icon: 'receipt-outline', activeIcon: 'receipt' },
  wallet:      { name: 'wallet',    labelKey: 'wallet.tabLabel',   a11yKey: 'a11y.tabWallet',  icon: 'wallet-outline',  activeIcon: 'wallet' },
  profile:     { name: 'profile',   labelKey: 'profile.tabLabel',  a11yKey: 'a11y.tabProfile', icon: 'person-outline',  activeIcon: 'person' },
};

const LEFT = ['home', 'my-orders'];
const RIGHT = ['wallet', 'profile'];

function TabButton({
  meta,
  focused,
  onPress,
}: {
  meta: TabMeta;
  focused: boolean;
  onPress: () => void;
}) {
  const { t } = useTranslation();
  // Fall back to a11y key label if the short label key isn't defined yet
  const label = t(meta.labelKey, { defaultValue: t(meta.a11yKey) });
  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="tab"
      accessibilityState={{ selected: focused }}
      accessibilityLabel={t(meta.a11yKey)}
      className="flex-1 items-center justify-center gap-1 py-1"
    >
      <Ionicons
        name={focused ? meta.activeIcon : meta.icon}
        size={23}
        color={focused ? '#4A552A' : '#A8A493'}
      />
      <Text className={`text-[11px] font-bold ${focused ? 'text-olive-700' : 'text-ink-faint'}`}>
        {label}
      </Text>
    </Pressable>
  );
}

/**
 * Action sheet launched from the central FAB so the customer can choose between
 * the laundry pickup flow and the point-to-point parcel flow.
 */
function CreateActionSheet({
  visible,
  onClose,
  onLaundry,
  onParcel,
}: {
  visible: boolean;
  onClose: () => void;
  onLaundry: () => void;
  onParcel: () => void;
}) {
  const { t } = useTranslation();
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
        {/* Stop propagation so taps inside the sheet don't dismiss it. */}
        <Pressable
          onPress={(e) => e.stopPropagation()}
          className="bg-cream px-5 pb-10 pt-5"
          style={{ borderTopLeftRadius: 28, borderTopRightRadius: 28 }}
        >
          <View className="mb-4 h-1.5 w-12 self-center rounded-full bg-cream-300" />
          <Text className="mb-4 text-lg font-extrabold text-ink">
            {t('parcel.chooseFlowTitle')}
          </Text>

          <Pressable
            onPress={onLaundry}
            accessibilityRole="button"
            accessibilityLabel={t('parcel.laundryOption')}
            className="mb-3 flex-row items-center rounded-2xl border border-cream-300 bg-white p-4"
          >
            <View className="mr-3 h-12 w-12 items-center justify-center rounded-2xl bg-olive-100">
              <Ionicons name="shirt-outline" size={24} color="#5C6A33" />
            </View>
            <View className="flex-1">
              <Text className="text-base font-extrabold text-ink">{t('parcel.laundryOption')}</Text>
              <Text className="mt-0.5 text-xs text-ink-muted">{t('parcel.laundryOptionSub')}</Text>
            </View>
            <Ionicons name="chevron-forward" size={18} color="#A8A493" />
          </Pressable>

          <Pressable
            onPress={onParcel}
            accessibilityRole="button"
            accessibilityLabel={t('parcel.parcelOption')}
            className="flex-row items-center rounded-2xl border border-cream-300 bg-white p-4"
          >
            <View className="mr-3 h-12 w-12 items-center justify-center rounded-2xl bg-gold-200">
              <Ionicons name="cube-outline" size={24} color="#8A641D" />
            </View>
            <View className="flex-1">
              <Text className="text-base font-extrabold text-ink">{t('parcel.parcelOption')}</Text>
              <Text className="mt-0.5 text-xs text-ink-muted">{t('parcel.parcelOptionSub')}</Text>
            </View>
            <Ionicons name="chevron-forward" size={18} color="#A8A493" />
          </Pressable>
        </Pressable>
      </Pressable>
    </Modal>
  );
}

function CustomTabBar({ state, navigation }: BottomTabBarProps) {
  const router = useRouter();
  const { t } = useTranslation();
  const setJobType = useBookingStore((s) => s.setJobType);
  const setFareQuote = useBookingStore((s) => s.setFareQuote);
  const setPickupAddress = useBookingStore((s) => s.setPickupAddress);
  const setDropAddress = useBookingStore((s) => s.setDropAddress);
  const activeName = state.routes[state.index]?.name;
  const [sheetVisible, setSheetVisible] = useState(false);

  const go = (name: string) => {
    navigation.navigate(name);
  };

  const openSheet = () => setSheetVisible(true);

  // Laundry entry is unchanged from the original FAB behaviour — just push the
  // items picker. (The laundry screens seed their own store state.)
  const startLaundry = () => {
    setSheetVisible(false);
    setJobType('laundry');
    router.push('/(app)/booking/items');
  };

  // Parcel entry starts a fresh parcel session so a prior abandoned run can't
  // leak stale addresses/quote into a new booking.
  const startParcel = () => {
    setSheetVisible(false);
    setJobType('parcel');
    setPickupAddress(null);
    setDropAddress(null);
    setFareQuote(null);
    router.push('/(app)/parcel/pickup');
  };

  return (
    <View style={{ overflow: 'visible' }}>
      {/* Extra top padding creates the visible ledge for the FAB above the bar */}
      <View
        className="flex-row items-end bg-white px-2"
        style={{
          paddingTop: 28,
          borderTopLeftRadius: 28,
          borderTopRightRadius: 28,
          overflow: 'visible',
          shadowColor: '#2E351C',
          shadowOpacity: 0.08,
          shadowRadius: 16,
          shadowOffset: { width: 0, height: -4 },
          elevation: 12,
        }}
      >
        <SafeAreaView edges={['bottom']} className="flex-1 flex-row items-end" style={{ overflow: 'visible' }}>
          {LEFT.map((n) => (
            <TabButton key={n} meta={TAB_META[n]} focused={activeName === n} onPress={() => go(n)} />
          ))}

          {/* Center FAB spacer — height matches the paddingTop gap so tabs align */}
          <View className="w-16 items-center" style={{ overflow: 'visible' }}>
            <Pressable
              onPress={openSheet}
              accessibilityRole="button"
              accessibilityLabel={t('a11y.schedulePickup')}
              className="absolute h-16 w-16 items-center justify-center rounded-full bg-gold-400"
              style={{
                top: -56,
                shadowColor: '#8A641D',
                shadowOpacity: 0.4,
                shadowRadius: 10,
                shadowOffset: { width: 0, height: 4 },
                elevation: 8,
              }}
            >
              <Ionicons name="add" size={32} color="#2E351C" />
            </Pressable>
          </View>

          {RIGHT.map((n) => (
            <TabButton key={n} meta={TAB_META[n]} focused={activeName === n} onPress={() => go(n)} />
          ))}
        </SafeAreaView>
      </View>

      <CreateActionSheet
        visible={sheetVisible}
        onClose={() => setSheetVisible(false)}
        onLaundry={startLaundry}
        onParcel={startParcel}
      />
    </View>
  );
}

export default function TabsLayout() {
  return (
    <Tabs
      screenOptions={{ headerShown: false }}
      tabBar={(props) => <CustomTabBar {...props} />}
    >
      <Tabs.Screen name="home" />
      <Tabs.Screen name="my-orders" />
      <Tabs.Screen name="wallet" />
      <Tabs.Screen name="profile" />
    </Tabs>
  );
}
