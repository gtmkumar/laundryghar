import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  cancelAccountDeletion,
  checkServiceability,
  createAddress,
  deleteAddress,
  getAccountDeletionRequest,
  getAddresses,
  getCategories,
  getPriceList,
  getProfile,
  getServices,
  requestAccountDeletion,
  updateAddress,
} from '@/api/catalog';
import type { CreateAddressRequest, CreateDeletionRequestRequest, UpdateAddressRequest } from '@/types/api';

export const catalogKeys = {
  categories:   ['catalog', 'categories'] as const,
  services:     (categoryId?: string) => ['catalog', 'services', categoryId ?? 'all'] as const,
  priceList:    ['catalog', 'price-list'] as const,
  addresses:    ['catalog', 'addresses'] as const,
  profile:      ['customer', 'profile'] as const,
};

export function useCustomerProfile() {
  return useQuery({
    queryKey: catalogKeys.profile,
    queryFn:  getProfile,
    staleTime: 5 * 60_000,
  });
}

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

export function useCreateAddress() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateAddressRequest) => createAddress(body),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: catalogKeys.addresses });
    },
  });
}

export function useUpdateAddress() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateAddressRequest }) =>
      updateAddress(id, body),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: catalogKeys.addresses });
    },
  });
}

export function useDeleteAddress() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteAddress(id),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: catalogKeys.addresses });
    },
  });
}

export function useServiceability(pincode: string) {
  return useQuery({
    queryKey: ['serviceability', pincode],
    queryFn:  () => checkServiceability(pincode),
    enabled:  pincode.length === 6 && /^\d{6}$/.test(pincode),
    staleTime: 5 * 60_000,
  });
}

// ── Account deletion ──────────────────────────────────────────────────────────

export function useAccountDeletionRequest() {
  return useQuery({
    queryKey: ['account', 'deletion-request'],
    queryFn:  getAccountDeletionRequest,
    staleTime: 60_000,
  });
}

export function useRequestAccountDeletion() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateDeletionRequestRequest) => requestAccountDeletion(body),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['account', 'deletion-request'] });
    },
  });
}

export function useCancelAccountDeletion() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: cancelAccountDeletion,
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['account', 'deletion-request'] });
    },
  });
}
