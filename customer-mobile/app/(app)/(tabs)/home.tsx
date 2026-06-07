/**
 * Home screen — wired to:
 *   GET {Catalog}/api/v1/customer/catalog/services   (services grid)
 *   GET {Engagement}/api/v1/public/banners?placement=home_top  (banner carousel)
 */
import React, { useRef, useState } from 'react';
import {
  Dimensions,
  FlatList,
  Image,
  Linking,
  Pressable,
  ScrollView,
  Text,
  View,
  ViewToken,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useServices } from '@/hooks/useCatalog';
import { useHomeBanners } from '@/hooks/useEngagement';
import { useAuthStore } from '@/store/authStore';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import type { AppBannerDto, ServiceDto } from '@/types/api';

// ---------------------------------------------------------------------------
// Banner deep-link helper
// Priority:
//   1. couponId  → /(app)/offers?couponId=<id>
//   2. promotionId (no customer endpoint) → /(app)/offers (list)
//   3. ctaDeeplink starting with '/'  → in-app push
//   4. ctaDeeplink / externalUrl http(s) → Linking.openURL
// ---------------------------------------------------------------------------

function resolveBannerPress(
  item: AppBannerDto,
  push: ReturnType<typeof useRouter>['push'],
): void {
  if (item.couponId) {
    push({
      pathname: '/(app)/offers' as never,
      params: { couponId: item.couponId },
    });
    return;
  }

  if (item.promotionId) {
    push('/(app)/offers' as never);
    return;
  }

  const deeplink = item.ctaDeeplink ?? null;
  if (deeplink) {
    if (deeplink.startsWith('/')) {
      // In-app relative route, e.g. "/(app)/price-list"
      push(deeplink as never);
      return;
    }
    if (/^https?:\/\//i.test(deeplink)) {
      // External http(s) URL — open in browser
      void Linking.openURL(deeplink).catch(() => undefined);
      return;
    }
    // Only the app's OWN registered scheme is handed to the OS. Banner fields
    // come from the CMS DB (admin-controlled), so we refuse arbitrary schemes
    // (intent://, file://, content://, tel:, …) that Linking.openURL would
    // otherwise honour — an open-redirect / unsafe-intent guard. Unknown
    // schemes are dropped (no-op) rather than invoked.
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

const { width: SCREEN_WIDTH } = Dimensions.get('window');

// ---------------------------------------------------------------------------
// Service card
// ---------------------------------------------------------------------------

const SERVICE_ICONS: Record<string, string> = {
  'dry clean':  '👔',
  'laundry':    '🧺',
  'steam iron': '♨️',
  'shoe':       '👟',
  'bag':        '👜',
  'carpet':     '🎨',
  'curtain':    '🪟',
};

function serviceIcon(name: string): string {
  const lower = name.toLowerCase();
  for (const [key, emoji] of Object.entries(SERVICE_ICONS)) {
    if (lower.includes(key)) return emoji;
  }
  return '🧹';
}

function ServiceCard({ item }: { item: ServiceDto }) {
  const router = useRouter();
  return (
    <Pressable
      onPress={() => router.push('/(app)/(tabs)/price-list')}
      accessibilityRole="button"
      accessibilityLabel={`View ${item.name} price list`}
      className="mb-3 mr-3 w-[44%] items-center rounded-2xl bg-white p-4 shadow-sm active:opacity-70"
      style={{ elevation: 2 }}
    >
      <Text
        style={{ fontSize: 36, marginBottom: 8 }}
        allowFontScaling={false}
        accessibilityElementsHidden
      >
        {serviceIcon(item.name)}
      </Text>
      <Text className="text-center text-sm font-semibold text-gray-800" numberOfLines={2}>
        {item.name}
      </Text>
    </Pressable>
  );
}

// ---------------------------------------------------------------------------
// Banner carousel — live CMS data with static fallback
// ---------------------------------------------------------------------------

interface BannerCardProps {
  item: AppBannerDto;
}

function BannerCard({ item }: BannerCardProps) {
  const hasImage = !!item.imageUrl;
  const bgColor = item.backgroundColor ?? '#1D4ED8';
  const router = useRouter();

  const handlePress = () => resolveBannerPress(item, router.push);

  return (
    <Pressable
      onPress={handlePress}
      accessibilityRole="button"
      accessibilityLabel={item.title ?? item.subtitle ?? 'Promotional banner'}
      style={{ width: SCREEN_WIDTH - 48 }}  // 48 = 2 × mx-6 (24px each)
      className="h-40 items-center justify-center rounded-3xl overflow-hidden active:opacity-80"
    >
      {hasImage ? (
        <Image
          source={{ uri: item.imageUrl }}
          style={{ width: '100%', height: '100%', borderRadius: 24 }}
          resizeMode="cover"
          accessibilityLabel={item.title ?? 'Banner'}
        />
      ) : (
        <View
          style={{ backgroundColor: bgColor, borderRadius: 24 }}
          className="flex-1 w-full items-center justify-center px-6"
        >
          {item.title ? (
            <Text className="text-lg font-bold text-white text-center">{item.title}</Text>
          ) : null}
          {item.subtitle ? (
            <Text className="mt-1 text-sm text-white/80 text-center">{item.subtitle}</Text>
          ) : null}
          {item.ctaText ? (
            <View className="mt-3 rounded-full bg-white/20 px-4 py-1">
              <Text className="text-sm font-semibold text-white">{item.ctaText}</Text>
            </View>
          ) : null}
        </View>
      )}
    </Pressable>
  );
}

/** Static fallback shown when there are no live banners */
function FallbackBanner() {
  return (
    <View className="h-40 items-center justify-center rounded-3xl bg-brand-700 px-6">
      <Text className="text-lg font-bold text-white">First Order 20% Off</Text>
      <Text className="mt-1 text-sm text-blue-200">Use code FIRST20</Text>
    </View>
  );
}

function BannerSection() {
  const { data: banners } = useHomeBanners('home_top');
  const [currentIndex, setCurrentIndex] = useState(0);
  const flatRef = useRef<FlatList<AppBannerDto>>(null);

  const onViewableItemsChanged = useRef(
    ({ viewableItems }: { viewableItems: ViewToken[] }) => {
      if (viewableItems[0]?.index != null) {
        setCurrentIndex(viewableItems[0].index);
      }
    },
  ).current;

  // No live banners — show the static fallback
  if (!banners || banners.length === 0) {
    return (
      <View className="mx-6 mb-6">
        <FallbackBanner />
      </View>
    );
  }

  return (
    <View className="mb-6">
      <FlatList
        ref={flatRef}
        data={banners}
        keyExtractor={(b) => b.id}
        horizontal
        pagingEnabled
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={{ paddingHorizontal: 24, gap: 12 }}
        onViewableItemsChanged={onViewableItemsChanged}
        viewabilityConfig={{ viewAreaCoveragePercentThreshold: 50 }}
        renderItem={({ item }) => <BannerCard item={item} />}
      />
      {/* Dots — only shown when there are multiple banners */}
      {banners.length > 1 && (
        <View className="mt-3 flex-row justify-center gap-1.5">
          {banners.map((_, i) => (
            <View
              key={i}
              className={`h-1.5 rounded-full ${
                i === currentIndex ? 'w-4 bg-brand-700' : 'w-1.5 bg-gray-300'
              }`}
            />
          ))}
        </View>
      )}
    </View>
  );
}

// ---------------------------------------------------------------------------
// Quick action
// ---------------------------------------------------------------------------

function QuickAction({ emoji, label, href }: { emoji: string; label: string; href: string }) {
  const router = useRouter();
  return (
    <Pressable
      onPress={() => router.push(href as never)}
      accessibilityRole="button"
      accessibilityLabel={label}
      className="flex-1 flex-row items-center gap-3 rounded-2xl border border-gray-200 bg-white p-4 active:opacity-70"
    >
      <Text style={{ fontSize: 24 }} allowFontScaling={false} accessibilityElementsHidden>{emoji}</Text>
      <Text className="font-semibold text-gray-800">{label}</Text>
    </Pressable>
  );
}

// ---------------------------------------------------------------------------
// Screen
// ---------------------------------------------------------------------------

export default function HomeScreen() {
  const { customer } = useAuthStore();
  const { data: services, isLoading, isError, refetch } = useServices();

  const displayName = customer?.displayName ?? customer?.firstName ?? 'there';

  if (isLoading) return <ScreenLoader />;
  if (isError)   return <ErrorState onRetry={() => void refetch()} />;

  return (
    <SafeAreaView className="flex-1 bg-surface-muted">
      <ScrollView showsVerticalScrollIndicator={false}>
        {/* Header */}
        <View className="bg-brand-700 px-6 pb-8 pt-6">
          <Text className="text-sm font-medium text-brand-200">Good day,</Text>
          <View className="flex-row items-center gap-1">
            <Text className="text-2xl font-bold text-white">
              Hi, {displayName}
            </Text>
            <Text style={{ fontSize: 22 }} allowFontScaling={false} accessibilityElementsHidden>
              {'\u{1F44B}'}
            </Text>
          </View>
          <Text className="mt-1 text-sm text-brand-200">
            What would you like to clean today?
          </Text>
        </View>

        {/* Banner — CMS-driven, falls back to static */}
        <View className="-mt-4">
          <BannerSection />
        </View>

        {/* Services grid */}
        <View className="px-6">
          <Text className="mb-4 text-lg font-bold text-gray-900">Our Services</Text>
          {services && services.length > 0 ? (
            <FlatList
              data={services}
              keyExtractor={(s) => s.id}
              numColumns={2}
              scrollEnabled={false}
              renderItem={({ item }) => <ServiceCard item={item} />}
              columnWrapperStyle={{ justifyContent: 'space-between' }}
            />
          ) : (
            <Text className="text-center text-base text-gray-500">
              No services available
            </Text>
          )}
        </View>

        {/* Quick actions */}
        <View className="mx-6 mt-6 mb-8">
          <Text className="mb-4 text-lg font-bold text-gray-900">Quick Actions</Text>
          <View className="flex-row gap-3">
            <QuickAction
              emoji="📦"
              label="My Orders"
              href="/(app)/(tabs)/my-orders"
            />
            <QuickAction
              emoji="💳"
              label="Packages"
              href="/(app)/(tabs)/price-list"
            />
          </View>
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}
