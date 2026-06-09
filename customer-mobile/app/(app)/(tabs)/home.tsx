/**
 * Home — greeting, address, promo banner, services grid, schedule-pickup card.
 * Wired to:
 *   GET {Catalog}/customer/catalog/services        (services grid)
 *   GET {Catalog}/customer/addresses               (address chip)
 *   GET {Engagement}/public/banners?placement=home_top  (promo banner)
 */
import React, { useState } from 'react';
import {
  Image,
  Linking,
  Pressable,
  ScrollView,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useServices, useAddresses } from '@/hooks/useCatalog';
import { useHomeBanners } from '@/hooks/useEngagement';
import { useAuthStore } from '@/store/authStore';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { serviceMeta } from '@/lib/serviceMeta';
import { greeting } from '@/lib/format';
import type { AppBannerDto, ServiceDto } from '@/types/api';

// Static services shown when the catalog is empty, so the grid always renders.
const FALLBACK_SERVICES: Pick<ServiceDto, 'id' | 'name'>[] = [
  { id: 'fb-dryclean', name: 'Dry Clean' },
  { id: 'fb-wash',     name: 'Wash + Fold' },
  { id: 'fb-steam',    name: 'Steam Iron' },
  { id: 'fb-shoes',    name: 'Shoes' },
  { id: 'fb-bags',     name: 'Bags' },
  { id: 'fb-curtains', name: 'Curtains' },
  { id: 'fb-carpet',   name: 'Carpet' },
];

function resolveBannerPress(
  item: AppBannerDto,
  push: ReturnType<typeof useRouter>['push'],
): void {
  if (item.couponId) {
    push({ pathname: '/(app)/offers' as never, params: { couponId: item.couponId } });
    return;
  }
  if (item.promotionId) {
    push('/(app)/offers' as never);
    return;
  }
  const deeplink = item.ctaDeeplink ?? null;
  if (deeplink) {
    if (deeplink.startsWith('/')) return push(deeplink as never);
    if (/^https?:\/\//i.test(deeplink)) {
      void Linking.openURL(deeplink).catch(() => undefined);
      return;
    }
    if (/^laundryghar:\/\//i.test(deeplink)) {
      void Linking.openURL(deeplink).catch(() => undefined);
      return;
    }
  }
  const external = item.externalUrl ?? null;
  if (external && /^https?:\/\//i.test(external)) {
    void Linking.openURL(external).catch(() => undefined);
  }
}

function ServiceTile({ name }: { name: string }) {
  const router = useRouter();
  const meta = serviceMeta(name);
  return (
    <Pressable
      onPress={() => router.push('/(app)/booking/items')}
      accessibilityRole="button"
      accessibilityLabel={`${name} service`}
      className="mb-4 items-center"
      style={{ width: '25%' }}
    >
      <View
        className="h-16 w-16 items-center justify-center rounded-2xl"
        style={{ backgroundColor: meta.bg }}
      >
        <Ionicons name={meta.icon} size={26} color={meta.tint} />
      </View>
      <Text className="mt-2 text-center text-xs font-semibold text-ink-soft" numberOfLines={1}>
        {name}
      </Text>
    </Pressable>
  );
}

function MoreTile() {
  const router = useRouter();
  return (
    <Pressable
      onPress={() => router.push('/(app)/price-list')}
      accessibilityRole="button"
      accessibilityLabel="More services"
      className="mb-4 items-center"
      style={{ width: '25%' }}
    >
      <View className="h-16 w-16 items-center justify-center rounded-2xl bg-cream-200">
        <Ionicons name="ellipsis-horizontal" size={26} color="#7B7A6C" />
      </View>
      <Text className="mt-2 text-center text-xs font-semibold text-ink-soft">More</Text>
    </Pressable>
  );
}

function PromoBanner({ banner }: { banner?: AppBannerDto }) {
  const router = useRouter();
  const [imgFailed, setImgFailed] = useState(false);

  if (banner) {
    const handlePress = () => resolveBannerPress(banner, router.push);
    if (banner.imageUrl && !imgFailed) {
      return (
        <Pressable onPress={handlePress} className="overflow-hidden rounded-3xl">
          <Image
            source={{ uri: banner.imageUrl }}
            style={{ width: '100%', height: 150 }}
            resizeMode="cover"
            onError={() => setImgFailed(true)}
          />
        </Pressable>
      );
    }
    return (
      <Pressable
        onPress={handlePress}
        className="overflow-hidden rounded-3xl p-5"
        style={{ backgroundColor: banner.backgroundColor ?? '#DBAC3D' }}
      >
        {banner.title ? <Text className="text-xl font-extrabold text-olive-900">{banner.title}</Text> : null}
        {banner.subtitle ? <Text className="mt-1 text-sm text-olive-900/80">{banner.subtitle}</Text> : null}
      </Pressable>
    );
  }

  // Themed fallback — matches the "20% off your first wash" mockup card.
  return (
    <Pressable
      onPress={() => router.push('/(app)/offers')}
      className="flex-row items-center overflow-hidden rounded-3xl bg-gold-300 p-5"
    >
      <View className="flex-1">
        <Text className="text-[11px] font-bold uppercase tracking-wider text-olive-900/70">First order</Text>
        <Text className="mt-1 text-2xl font-extrabold text-olive-900">20% off your first wash</Text>
        <Text className="mt-1 text-xs text-olive-900/70">Code FRESH20 · ends in 6 days</Text>
      </View>
      <Ionicons name="gift" size={44} color="#8A641D" />
    </Pressable>
  );
}

export default function HomeScreen() {
  const router = useRouter();
  const { customer } = useAuthStore();
  const { data: services, isLoading, isError, refetch } = useServices();
  const { data: banners } = useHomeBanners('home_top');
  const { data: addresses } = useAddresses();

  const displayName = customer?.displayName ?? customer?.firstName ?? 'there';
  const defaultAddr = addresses?.find((a) => a.isDefault) ?? addresses?.[0];
  const addrLabel = defaultAddr
    ? `${defaultAddr.label ?? defaultAddr.line1}`
    : 'Add a pickup address';

  if (isLoading) return <ScreenLoader />;
  if (isError) return <ErrorState onRetry={() => void refetch()} />;

  const grid = services && services.length > 0 ? services : FALLBACK_SERVICES;

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 28 }}>
        {/* Header */}
        <View className="flex-row items-center justify-between px-6 pt-2">
          <View className="flex-row items-center gap-3">
            <View className="h-10 w-10 items-center justify-center rounded-xl bg-white">
              <Ionicons name="menu" size={22} color="#3C3F35" />
            </View>
            <View>
              <Text className="text-xs text-ink-muted">{greeting()}</Text>
              <Text className="text-lg font-extrabold text-ink">{displayName} 👋</Text>
            </View>
          </View>
          <Pressable
            onPress={() => router.push('/(app)/offers')}
            className="h-10 w-10 items-center justify-center rounded-xl bg-white"
            accessibilityLabel="Notifications"
          >
            <Ionicons name="notifications-outline" size={20} color="#3C3F35" />
          </Pressable>
        </View>

        {/* Address selector */}
        <Pressable className="mx-6 mt-4 flex-row items-center gap-2" accessibilityLabel="Pickup address">
          <Ionicons name="location-sharp" size={16} color="#5C6A33" />
          <Text className="text-sm font-bold text-ink-soft" numberOfLines={1}>
            {addrLabel}
          </Text>
          <Ionicons name="chevron-down" size={16} color="#7B7A6C" />
        </Pressable>

        {/* Promo */}
        <View className="mx-6 mt-4">
          <PromoBanner banner={banners?.[0]} />
        </View>

        {/* Services */}
        <View className="mx-6 mt-7">
          <View className="mb-4 flex-row items-center justify-between">
            <Text className="text-lg font-extrabold text-ink">Our services</Text>
            <Pressable onPress={() => router.push('/(app)/price-list')} hitSlop={6} className="flex-row items-center gap-1">
              <Text className="text-sm font-bold text-olive-700">See prices</Text>
              <Ionicons name="arrow-forward" size={14} color="#4A552A" />
            </Pressable>
          </View>
          <View className="flex-row flex-wrap">
            {grid.slice(0, 7).map((s) => (
              <ServiceTile key={s.id} name={s.name} />
            ))}
            <MoreTile />
          </View>
        </View>

        {/* Schedule a pickup card */}
        <Pressable
          onPress={() => router.push('/(app)/booking/items')}
          className="mx-6 mt-3 overflow-hidden rounded-3xl bg-olive-700 p-5"
        >
          <Text className="text-[11px] font-bold uppercase tracking-wider text-olive-100">Free pickup</Text>
          <Text className="mt-1 text-2xl font-extrabold text-white">Schedule a pickup</Text>
          <Text className="mt-1 text-sm text-olive-100">
            Pick your items, choose a slot — a rider collects from your door.
          </Text>
          <View className="mt-4 flex-row items-center gap-2 self-start rounded-full bg-gold-400 px-4 py-2.5">
            <Text className="text-sm font-extrabold text-olive-900">Start now</Text>
            <Ionicons name="arrow-forward" size={16} color="#2E351C" />
          </View>
        </Pressable>
      </ScrollView>
    </SafeAreaView>
  );
}
