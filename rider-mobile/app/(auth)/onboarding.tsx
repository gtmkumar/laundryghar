/**
 * Rider onboarding carousel — data driven by the Engagement CMS.
 * Fetches slides from GET /api/v1/public/onboarding-slides?appType=rider.
 * Falls back to static FALLBACK_SLIDES when the network is unavailable
 * or the server returns no slides, so the app always works offline.
 *
 * Flow: onboarding → login (rider must sign in after the carousel).
 */
import React, { useRef, useState } from 'react';
import {
  ActivityIndicator,
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
import { Button } from '@/components/ui/Button';
import { useOnboardingSlides } from '@/hooks/useEngagement';
import type { OnboardingSlideDto } from '@/types/api';

const { width: SCREEN_WIDTH } = Dimensions.get('window');

// ---------------------------------------------------------------------------
// Static fallback — shown when CMS data is unavailable
// ---------------------------------------------------------------------------

const FALLBACK_SLIDES: OnboardingSlideDto[] = [
  {
    id: 'fallback-1',
    brandId: '',
    appType: 'rider',
    title: 'Welcome Rider',
    titleLocalized: '',
    description: 'Manage your pickups and deliveries efficiently.',
    descriptionLocalized: '',
    imageUrl: '',
    displayOrder: 1,
    isActive: true,
    status: 'active',
    createdAt: '',
    updatedAt: '',
  },
  {
    id: 'fallback-2',
    brandId: '',
    appType: 'rider',
    title: 'View Assigned Orders',
    titleLocalized: '',
    description: 'See all your assigned pickup and delivery tasks.',
    descriptionLocalized: '',
    imageUrl: '',
    displayOrder: 2,
    isActive: true,
    status: 'active',
    createdAt: '',
    updatedAt: '',
  },
  {
    id: 'fallback-3',
    brandId: '',
    appType: 'rider',
    title: 'Update Delivery Status',
    titleLocalized: '',
    description: 'Mark orders picked up, in-transit, or delivered.',
    descriptionLocalized: '',
    imageUrl: '',
    displayOrder: 3,
    isActive: true,
    status: 'active',
    createdAt: '',
    updatedAt: '',
  },
];

// Background colors cycled when the CMS slide has no backgroundColor set
const CYCLE_COLORS = ['#F0FDF4', '#DCFCE7', '#ECFDF5', '#F0FDF4', '#D1FAE5'];

// ---------------------------------------------------------------------------
// SlideItem
// ---------------------------------------------------------------------------

interface SlideItemProps {
  item: OnboardingSlideDto;
  index: number;
}

function SlideItem({ item, index }: SlideItemProps) {
  const bgColor = item.backgroundColor ?? CYCLE_COLORS[index % CYCLE_COLORS.length];
  const hasImage = !!item.imageUrl;

  return (
    <View
      style={{ width: SCREEN_WIDTH }}
      className="items-center justify-center px-8"
    >
      <View
        className="mb-8 h-64 w-64 items-center justify-center rounded-3xl overflow-hidden"
        style={{ backgroundColor: bgColor }}
        accessibilityLabel={`${item.title} illustration`}
      >
        {hasImage ? (
          <Image
            source={{ uri: item.imageUrl }}
            style={{ width: '100%', height: '100%' }}
            resizeMode="cover"
            accessibilityLabel={item.title}
          />
        ) : (
          <Text className="text-6xl" accessibilityElementsHidden>
            {index === 0 ? '🛵' : index === 1 ? '📋' : '✅'}
          </Text>
        )}
      </View>

      <Text
        className="mb-3 text-center text-2xl font-bold text-gray-900"
        style={item.textColor ? { color: item.textColor } : undefined}
      >
        {item.title}
      </Text>

      <Text className="text-center text-base leading-6 text-gray-500">
        {item.description ?? ''}
      </Text>
    </View>
  );
}

// ---------------------------------------------------------------------------
// Screen
// ---------------------------------------------------------------------------

export default function OnboardingScreen() {
  const router = useRouter();
  const [currentIndex, setCurrentIndex] = useState(0);
  const flatRef = useRef<FlatList<OnboardingSlideDto>>(null);

  const { data: cmsSlides, isLoading } = useOnboardingSlides('rider');

  const slides: OnboardingSlideDto[] =
    cmsSlides && cmsSlides.length > 0 ? cmsSlides : FALLBACK_SLIDES;

  const onViewableItemsChanged = useRef(
    ({ viewableItems }: { viewableItems: ViewToken[] }) => {
      if (viewableItems[0]?.index != null) {
        setCurrentIndex(viewableItems[0].index);
      }
    },
  ).current;

  const goNext = () => {
    if (currentIndex < slides.length - 1) {
      flatRef.current?.scrollToIndex({ index: currentIndex + 1, animated: true });
    } else {
      router.replace('/(auth)/login');
    }
  };

  const isLast = currentIndex === slides.length - 1;

  return (
    <SafeAreaView className="flex-1 bg-white">
      {/* Skip */}
      <View className="flex-row items-center justify-end px-6 pt-4">
        {isLoading && (
          <ActivityIndicator size="small" color="#15803D" style={{ marginRight: 8 }} />
        )}
        <Pressable
          onPress={() => router.replace('/(auth)/login')}
          accessibilityRole="button"
          accessibilityLabel="Skip onboarding"
          hitSlop={{ top: 10, bottom: 10, left: 10, right: 10 }}
        >
          <Text className="text-base font-medium text-brand-700">Skip</Text>
        </Pressable>
      </View>

      {/* Slides */}
      <FlatList
        ref={flatRef}
        data={slides}
        keyExtractor={(item) => item.id}
        horizontal
        pagingEnabled
        showsHorizontalScrollIndicator={false}
        onViewableItemsChanged={onViewableItemsChanged}
        viewabilityConfig={{ viewAreaCoveragePercentThreshold: 50 }}
        renderItem={({ item, index }) => <SlideItem item={item} index={index} />}
      />

      {/* Dots */}
      <View className="flex-row justify-center gap-2 py-4">
        {slides.map((_, i) => (
          <View
            key={i}
            className={`h-2 rounded-full ${
              i === currentIndex ? 'w-6 bg-brand-700' : 'w-2 bg-gray-300'
            }`}
          />
        ))}
      </View>

      {/* CTA */}
      <View className="px-6 pb-6">
        <Button
          title={isLast ? 'Sign In' : 'Next'}
          onPress={goNext}
          fullWidth
          size="lg"
        />
      </View>
    </SafeAreaView>
  );
}
