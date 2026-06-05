/**
 * Price List screen — wired to:
 *   GET {Catalog}/api/v1/customer/catalog/categories
 *   GET {Catalog}/api/v1/customer/catalog/price-list
 * Filterable by category, searchable by item name.
 */
import React, { useMemo, useState } from 'react';
import {
  FlatList,
  Pressable,
  ScrollView,
  Text,
  TextInput,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useCategories, usePriceList } from '@/hooks/useCatalog';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { EmptyState } from '@/components/ui/EmptyState';
import type { PriceListItemDto } from '@/types/api';

function PriceRow({ item }: { item: PriceListItemDto }) {
  return (
    <View className="flex-row items-center justify-between border-b border-gray-100 py-3">
      <View className="flex-1 mr-4">
        <Text className="text-sm font-semibold text-gray-900">{item.itemName}</Text>
        <Text className="text-xs text-gray-500">
          {[item.categoryName, item.serviceName, item.fabricTypeName]
            .filter(Boolean)
            .join(' · ')}
        </Text>
      </View>
      <Text className="text-base font-bold text-brand-700">
        ₹{item.price.toFixed(0)}
        {item.unit ? <Text className="text-xs font-normal text-gray-400"> /{item.unit}</Text> : null}
      </Text>
    </View>
  );
}

export default function PriceListScreen() {
  const { data: categories, isLoading: catsLoading, isError: catsError, refetch: refetchCats } = useCategories();
  const { data: priceList,  isLoading: plLoading,   isError: plError,   refetch: refetchPl   } = usePriceList();

  const [selectedCategoryId, setSelectedCategoryId] = useState<string | null>(null);
  const [search, setSearch] = useState('');

  const isLoading = catsLoading || plLoading;
  const isError   = catsError || plError;

  const filtered = useMemo(() => {
    if (!priceList) return [];
    let items = priceList.filter((i) => i.isActive);
    if (selectedCategoryId) {
      items = items.filter((i) => i.categoryId === selectedCategoryId);
    }
    if (search.trim()) {
      const q = search.toLowerCase();
      items = items.filter(
        (i) =>
          i.itemName.toLowerCase().includes(q) ||
          i.serviceName.toLowerCase().includes(q) ||
          i.categoryName?.toLowerCase().includes(q),
      );
    }
    return items;
  }, [priceList, selectedCategoryId, search]);

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
    <SafeAreaView className="flex-1 bg-white">
      {/* Header */}
      <View className="px-6 pt-6 pb-4 border-b border-gray-100">
        <Text className="mb-4 text-2xl font-bold text-gray-900">Price List</Text>

        {/* Search */}
        <View className="flex-row items-center rounded-xl bg-gray-100 px-3 mb-4">
          <Text className="mr-2 text-gray-400" accessibilityElementsHidden>🔍</Text>
          <TextInput
            value={search}
            onChangeText={setSearch}
            placeholder="Search items…"
            placeholderTextColor="#9CA3AF"
            className="flex-1 py-2 text-base text-gray-900"
            accessibilityLabel="Search price list"
            returnKeyType="search"
            clearButtonMode="while-editing"
          />
        </View>

        {/* Category chips */}
        <ScrollView horizontal showsHorizontalScrollIndicator={false}>
          <Pressable
            onPress={() => setSelectedCategoryId(null)}
            accessibilityRole="button"
            accessibilityLabel="All categories"
            accessibilityState={{ selected: selectedCategoryId === null }}
            className={[
              'mr-2 rounded-full px-4 py-2',
              selectedCategoryId === null
                ? 'bg-brand-700'
                : 'border border-gray-300 bg-white',
            ].join(' ')}
          >
            <Text
              className={`text-sm font-medium ${selectedCategoryId === null ? 'text-white' : 'text-gray-700'}`}
            >
              All
            </Text>
          </Pressable>
          {categories?.map((cat) => {
            const selected = selectedCategoryId === cat.id;
            return (
              <Pressable
                key={cat.id}
                onPress={() =>
                  setSelectedCategoryId(selected ? null : cat.id)
                }
                accessibilityRole="button"
                accessibilityLabel={cat.name}
                accessibilityState={{ selected }}
                className={[
                  'mr-2 rounded-full px-4 py-2',
                  selected ? 'bg-brand-700' : 'border border-gray-300 bg-white',
                ].join(' ')}
              >
                <Text
                  className={`text-sm font-medium ${selected ? 'text-white' : 'text-gray-700'}`}
                >
                  {cat.name}
                </Text>
              </Pressable>
            );
          })}
        </ScrollView>
      </View>

      {/* List */}
      {filtered.length === 0 ? (
        <EmptyState
          title="No items found"
          message={search ? 'Try a different search term' : 'No prices available for this category'}
        />
      ) : (
        <FlatList
          data={filtered}
          keyExtractor={(i) => i.id}
          renderItem={({ item }) => <PriceRow item={item} />}
          contentContainerStyle={{ paddingHorizontal: 24, paddingBottom: 24 }}
          showsVerticalScrollIndicator={false}
          ItemSeparatorComponent={() => null}
          ListHeaderComponent={
            <Text className="mt-4 mb-1 text-xs font-medium uppercase tracking-wider text-gray-400">
              {filtered.length} items
            </Text>
          }
        />
      )}
    </SafeAreaView>
  );
}
