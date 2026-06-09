/**
 * Price list — searchable, category-filterable catalogue.
 *   GET {Catalog}/customer/catalog/categories
 *   GET {Catalog}/customer/catalog/services
 *   GET {Catalog}/customer/catalog/price-list
 */
import React, { useMemo, useState } from 'react';
import { FlatList, Pressable, ScrollView, Text, TextInput, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useCategories, usePriceList, useServices } from '@/hooks/useCatalog';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { EmptyState } from '@/components/ui/EmptyState';
import { Chip } from '@/components/ui/Chip';
import { rupees } from '@/lib/format';
import type { PriceListItemDto } from '@/types/api';

function PriceRow({ item }: { item: PriceListItemDto }) {
  const label = item.displayLabel ?? item.notes ?? 'Garment';
  return (
    <View className="flex-row items-center justify-between border-b border-cream-200 py-3.5">
      <View className="mr-4 flex-1">
        <Text className="text-base font-bold text-ink">{label}</Text>
        {item.notes ? <Text className="text-xs text-ink-muted">{item.notes}</Text> : null}
      </View>
      <View className="items-end">
        <Text className="text-base font-extrabold text-olive-700">{rupees(item.basePrice)}</Text>
        {item.expressPrice ? (
          <Text className="text-[11px] text-gold-600">Express {rupees(item.expressPrice)}</Text>
        ) : null}
      </View>
    </View>
  );
}

export default function PriceListScreen() {
  const router = useRouter();
  const { data: categories, isLoading: catsLoading, isError: catsError, refetch: refetchCats } = useCategories();
  const { data: services } = useServices();
  const { data: priceList, isLoading: plLoading, isError: plError, refetch: refetchPl } = usePriceList();

  const [selectedCategoryId, setSelectedCategoryId] = useState<string | null>(null);
  const [search, setSearch] = useState('');

  const isLoading = catsLoading || plLoading;
  const isError = catsError || plError;

  const serviceIdsForCategory = useMemo<Set<string> | null>(() => {
    if (!selectedCategoryId || !services) return null;
    return new Set(services.filter((s) => s.categoryId === selectedCategoryId).map((s) => s.id));
  }, [selectedCategoryId, services]);

  const filtered = useMemo(() => {
    if (!priceList) return [];
    let items = priceList.filter((i) => i.isActive);
    if (serviceIdsForCategory) items = items.filter((i) => serviceIdsForCategory.has(i.serviceId));
    if (search.trim()) {
      const q = search.toLowerCase();
      items = items.filter(
        (i) => (i.displayLabel ?? '').toLowerCase().includes(q) || (i.notes ?? '').toLowerCase().includes(q),
      );
    }
    return items;
  }, [priceList, serviceIdsForCategory, search]);

  if (isLoading) return <ScreenLoader />;
  if (isError) {
    return (
      <ErrorState
        onRetry={() => {
          void refetchCats();
          void refetchPl();
        }}
      />
    );
  }

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      {/* Header */}
      <View className="px-5 pt-2">
        <View className="flex-row items-center gap-3 pb-3">
          <Pressable
            onPress={() => router.back()}
            className="h-10 w-10 items-center justify-center rounded-full bg-white"
            accessibilityLabel="Go back"
          >
            <Ionicons name="chevron-back" size={22} color="#3C3F35" />
          </Pressable>
          <Text className="text-xl font-extrabold text-ink">Price list</Text>
        </View>

        {/* Search */}
        <View className="mb-3 flex-row items-center rounded-2xl border border-cream-300 bg-white px-3">
          <Ionicons name="search" size={18} color="#A8A493" />
          <TextInput
            value={search}
            onChangeText={setSearch}
            placeholder="Search items…"
            placeholderTextColor="#A8A493"
            className="flex-1 py-3 pl-2 text-base text-ink"
            returnKeyType="search"
            clearButtonMode="while-editing"
          />
        </View>

        {/* Category chips */}
        <ScrollView horizontal showsHorizontalScrollIndicator={false} className="mb-2">
          <Chip label="All" selected={selectedCategoryId === null} onPress={() => setSelectedCategoryId(null)} />
          {categories?.map((cat) => (
            <Chip
              key={cat.id}
              label={cat.name}
              selected={selectedCategoryId === cat.id}
              onPress={() => setSelectedCategoryId(selectedCategoryId === cat.id ? null : cat.id)}
            />
          ))}
        </ScrollView>
      </View>

      {filtered.length === 0 ? (
        <EmptyState
          icon="pricetags-outline"
          title="No items found"
          message={search ? 'Try a different search term' : 'No prices available for this category'}
        />
      ) : (
        <FlatList
          data={filtered}
          keyExtractor={(i) => i.id}
          renderItem={({ item }) => <PriceRow item={item} />}
          contentContainerStyle={{ paddingHorizontal: 20, paddingBottom: 32 }}
          showsVerticalScrollIndicator={false}
          ListHeaderComponent={
            <Text className="mb-1 mt-2 text-xs font-bold uppercase tracking-wider text-ink-faint">
              {filtered.length} items
            </Text>
          }
        />
      )}
    </SafeAreaView>
  );
}
