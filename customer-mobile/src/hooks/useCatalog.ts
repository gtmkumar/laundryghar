import { useQuery } from '@tanstack/react-query';
import { getCategories, getServices, getPriceList, getAddresses } from '@/api/catalog';

export const catalogKeys = {
  categories:   ['catalog', 'categories'] as const,
  services:     (categoryId?: string) => ['catalog', 'services', categoryId ?? 'all'] as const,
  priceList:    ['catalog', 'price-list'] as const,
  addresses:    ['catalog', 'addresses'] as const,
};

export function useCategories() {
  return useQuery({
    queryKey: catalogKeys.categories,
    queryFn:  getCategories,
    staleTime: 5 * 60 * 1_000,
  });
}

export function useServices(categoryId?: string) {
  return useQuery({
    queryKey: catalogKeys.services(categoryId),
    queryFn:  () => getServices(categoryId),
    staleTime: 5 * 60 * 1_000,
  });
}

export function usePriceList() {
  return useQuery({
    queryKey: catalogKeys.priceList,
    queryFn:  getPriceList,
    staleTime: 5 * 60 * 1_000,
  });
}

export function useAddresses() {
  return useQuery({
    queryKey: catalogKeys.addresses,
    queryFn:  getAddresses,
    staleTime: 60_000,
  });
}
