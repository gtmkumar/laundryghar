/**
 * Onboarding carousel — CMS-driven (GET /public/onboarding-slides?appType=customer)
 * with a themed static fallback so the app always works offline.
 * Matches the v2 mockup: illustration up top, bottom sheet with eyebrow + big
 * title + subtitle, Skip / Next controls and progress dots.
 */
import React, { useRef, useState } from 'react';
import {
  Dimensions,
  FlatList,
  Image,
  Pressable,
  Text,
  View,
  ViewToken,
} from 'react-native';
import { useRouter } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import { StatusBar } from 'expo-status-bar';
import { LinearGradient } from 'expo-linear-gradient';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Button } from '@/components/ui/Button';
import { useAuthStore } from '@/store/authStore';
import { useOnboardingSlides } from '@/hooks/useEngagement';
import { useTranslation } from 'react-i18next';
import { pickLocalized } from '@/i18n';
import type { OnboardingSlideDto } from '@/types/api';

const { width: SCREEN_WIDTH } = Dimensions.get('window');

const FALLBACK_SLIDES: OnboardingSlideDto[] = [
  {
    id: 'fb-1', brandId: '', appType: 'customer',
    title: '1100+ stores. 400+ cities. One Ghar.',
    titleLocalized: '',
    description: "From DLF Phase 4 to your maa's village — we wash where you live.",
    descriptionLocalized: '', imageUrl: '', displayOrder: 1, isActive: true,
    status: 'active', createdAt: '', updatedAt: '',
  },
  {
    id: 'fb-2', brandId: '', appType: 'customer',
    title: 'Doorstep pickup, garment-by-garment care.',
    titleLocalized: '',
    description: 'Schedule a pickup in seconds. A rider collects from your door — no queues.',
    descriptionLocalized: '', imageUrl: '', displayOrder: 2, isActive: true,
    status: 'active', createdAt: '', updatedAt: '',
  },
  {
    id: 'fb-3', brandId: '', appType: 'customer',
    title: "Track every piece till it's back home.",
    titleLocalized: '',
    description: 'Live timeline from pickup to delivery, with WhatsApp updates on the way.',
    descriptionLocalized: '', imageUrl: '', displayOrder: 3, isActive: true,
    status: 'active', createdAt: '', updatedAt: '',
  },
];

const SLIDE_ICONS = ['map-marker-radius', 'truck-fast-outline', 'progress-check'] as const;

function Illustration({ index, imageUrl }: { index: number; imageUrl?: string }) {
  const [imgFailed, setImgFailed] = useState(false);

  if (imageUrl && !imgFailed) {
    return (
      <Image
        source={{ uri: imageUrl }}
        style={{ width: SCREEN_WIDTH * 0.7, height: SCREEN_WIDTH * 0.7, borderRadius: 32 }}
        resizeMode="cover"
        onError={() => setImgFailed(true)}
      />
    );
  }
  // Olive "map blob" with a Gurugram pin — evokes the mockup's hero.
  return (
    <View className="items-center justify-center">
      <View
        className="items-center justify-center rounded-[80px] bg-olive-600"
        style={{ width: SCREEN_WIDTH * 0.62, height: SCREEN_WIDTH * 0.62 }}
      >
        <MaterialCommunityIcons
          name={SLIDE_ICONS[index % SLIDE_ICONS.length]}
          size={88}
          color="#F3EEE3"
        />
      </View>
      <View className="absolute -right-1 top-6 flex-row items-center gap-1 rounded-full bg-white px-3 py-1.5 shadow">
        <View className="h-2 w-2 rounded-full bg-gold-400" />
        <Text className="text-xs font-bold text-ink-soft">Gurugram</Text>
      </View>
    </View>
  );
}

function Slide({ item, index }: { item: OnboardingSlideDto; index: number }) {
  return (
    <View style={{ width: SCREEN_WIDTH }} className="flex-1 items-center justify-center px-6">
      <Illustration index={index} imageUrl={item.imageUrl || undefined} />
    </View>
  );
}

export default function OnboardingScreen() {
  const router = useRouter();
  const { t } = useTranslation();
  const [index, setIndex] = useState(0);
  const flatRef = useRef<FlatList<OnboardingSlideDto>>(null);

  const { data: cmsSlides } = useOnboardingSlides('customer');
  const setHasOnboarded = useAuthStore((s) => s.setHasOnboarded);
  const slides = cmsSlides && cmsSlides.length > 0 ? cmsSlides : FALLBACK_SLIDES;
  const current = slides[index] ?? slides[0];
  const isLast = index === slides.length - 1;

  const onViewable = useRef(({ viewableItems }: { viewableItems: ViewToken[] }) => {
    if (viewableItems[0]?.index != null) setIndex(viewableItems[0].index);
  }).current;

  // Persist the flag so returning users land directly on login next time.
  const finishOnboarding = () => {
    void setHasOnboarded();
    router.replace('/(auth)/phone');
  };

  const goNext = () => {
    if (!isLast) {
      flatRef.current?.scrollToIndex({ index: index + 1, animated: true });
    } else {
      finishOnboarding();
    }
  };

  return (
    <View className="flex-1 bg-cream">
      <StatusBar style="dark" />
      <LinearGradient
        colors={['#F3EEE3', '#FAF7F0']}
        style={{ flex: 1 }}
      >
        <SafeAreaView className="flex-1" edges={['top']}>
          {/* Top illustration carousel */}
          <View className="flex-[1.1]">
            <FlatList
              ref={flatRef}
              data={slides}
              keyExtractor={(s) => s.id}
              horizontal
              pagingEnabled
              showsHorizontalScrollIndicator={false}
              onViewableItemsChanged={onViewable}
              viewabilityConfig={{ viewAreaCoveragePercentThreshold: 50 }}
              renderItem={({ item, index: i }) => <Slide item={item} index={i} />}
            />
          </View>

          {/* Bottom sheet with copy + controls */}
          <View className="rounded-t-[36px] bg-white px-7 pb-8 pt-8" style={{
            shadowColor: '#2E351C', shadowOpacity: 0.08, shadowRadius: 20, shadowOffset: { width: 0, height: -6 },
          }}>
            <Text className="mb-2 text-xs font-bold uppercase tracking-[3px] text-gold-600">{t('onboarding.eyebrow')}</Text>
            <Text className="text-3xl font-extrabold leading-9 text-ink">{pickLocalized(current.title, current.titleLocalized)}</Text>
            <Text className="mt-3 text-base leading-6 text-ink-muted">{pickLocalized(current.description ?? '', current.descriptionLocalized)}</Text>

            {/* Dots */}
            <View className="mt-6 flex-row gap-1.5">
              {slides.map((_, i) => (
                <View
                  key={i}
                  className={`h-2 rounded-full ${i === index ? 'w-6 bg-olive-600' : 'w-2 bg-cream-300'}`}
                />
              ))}
            </View>

            {/* Controls */}
            <View className="mt-7 flex-row gap-3">
              <View className="flex-1">
                <Button
                  title={t('onboarding.skip')}
                  variant="secondary"
                  size="lg"
                  fullWidth
                  onPress={finishOnboarding}
                />
              </View>
              <View className="flex-1">
                <Button
                  title={isLast ? t('onboarding.getStarted') : t('onboarding.next')}
                  size="lg"
                  fullWidth
                  iconRight="arrow-forward"
                  onPress={goNext}
                />
              </View>
            </View>
          </View>
        </SafeAreaView>
      </LinearGradient>
    </View>
  );
}
