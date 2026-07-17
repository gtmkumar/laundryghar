import { Alert } from 'react-native';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import i18n from '@/i18n';
import {
  cancelAccountDeletion,
  checkServiceability,
  createAddress,
  deleteAddress,
  getAccountDeletionRequest,
  getAddresses,
  getCatalogConfig,
  getCategories,
  getMyConsents,
  getPriceList,
  getProfile,
  getServices,
  grantConsent,
  requestAccountDeletion,
  updateAddress,
} from '@/api/catalog';
import type {
  CreateAddressRequest,
  CreateDeletionRequestRequest,
  CustomerAddressDto,
  DpdpConsentDto,
  GrantConsentRequest,
  UpdateAddressRequest,
} from '@/types/api';

export const catalogKeys = {
  categories:   ['catalog', 'categories'] as const,
  services:     (categoryId?: string) => ['catalog', 'services', categoryId ?? 'all'] as const,
  priceList:    ['catalog', 'price-list'] as const,
  config:       (storeId?: string) => ['catalog', 'config', storeId ?? 'brand'] as const,
  addresses:    ['catalog', 'addresses'] as const,
  profile:      ['customer', 'profile'] as const,
};

/**
 * Brand/store business rules that gate the booking flow (min order value,
 * currency, high-value threshold). Cached per store; changing storeId refetches.
 * Config changes rarely, so it is cached aggressively.
 */
export function useCatalogConfig(storeId?: string) {
  return useQuery({
    queryKey: catalogKeys.config(storeId),
    queryFn:  () => getCatalogConfig(storeId),
    staleTime: 10 * 60_000,
  });
}

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
  // CUST-BUG-02: invalidateQueries fires in useCreateAddress.onSuccess, but the
  // booking pickup screen mounts useAddresses before the add-address modal opens.
  // refetchOnWindowFocus ensures the picker re-fetches when the modal closes and
  // the booking screen regains focus, so the new address appears immediately.
  return useQuery({
    queryKey: catalogKeys.addresses,
    queryFn:  getAddresses,
    staleTime: 60_000,
    refetchOnWindowFocus: true,
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

/**
 * Set an address as the default. Optimistically flips isDefault on the target
 * (and off every other address) so the picker updates instantly. Dedicated hook
 * — kept separate from useUpdateAddress so the full-edit path (which the edit
 * form already error-Alerts) doesn't double-Alert on rollback.
 */
export function useSetDefaultAddress() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateAddressRequest }) =>
      updateAddress(id, body),
    onMutate: async ({ id }) => {
      await qc.cancelQueries({ queryKey: catalogKeys.addresses });
      const previous = qc.getQueryData<CustomerAddressDto[]>(catalogKeys.addresses);
      qc.setQueryData<CustomerAddressDto[]>(catalogKeys.addresses, (prev) =>
        prev?.map((a) => ({ ...a, isDefault: a.id === id })),
      );
      return { previous };
    },
    onError: (err, _vars, ctx) => {
      if (ctx?.previous) qc.setQueryData(catalogKeys.addresses, ctx.previous);
      Alert.alert(
        i18n.t('error.generic'),
        err instanceof Error ? err.message : i18n.t('error.tryAgain'),
      );
    },
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: catalogKeys.addresses });
    },
  });
}

export function useDeleteAddress() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteAddress(id),
    onMutate: async (id) => {
      await qc.cancelQueries({ queryKey: catalogKeys.addresses });
      const previous = qc.getQueryData<CustomerAddressDto[]>(catalogKeys.addresses);
      qc.setQueryData<CustomerAddressDto[]>(catalogKeys.addresses, (prev) =>
        prev?.filter((a) => a.id !== id),
      );
      return { previous };
    },
    // Consumer (addresses screen) surfaces the rollback Alert; here we only restore.
    onError: (_err, _id, ctx) => {
      if (ctx?.previous) qc.setQueryData(catalogKeys.addresses, ctx.previous);
    },
    onSettled: () => {
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

// ── DPDP consents ─────────────────────────────────────────────────────────────

export function useMyConsents() {
  return useQuery({
    queryKey: ['customer', 'consents'],
    queryFn:  getMyConsents,
    staleTime: 5 * 60_000,
  });
}

export function useGrantConsent() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: GrantConsentRequest) => grantConsent(req),
    onMutate: async (req) => {
      await qc.cancelQueries({ queryKey: ['customer', 'consents'] });
      const previous = qc.getQueryData<DpdpConsentDto[]>(['customer', 'consents']);
      qc.setQueryData<DpdpConsentDto[]>(['customer', 'consents'], (prev) =>
        prev?.map((c) =>
          c.purpose === req.purpose
            ? {
                ...c,
                consentStatus: 'granted',
                grantedAt: new Date().toISOString(),
                withdrawnAt: null,
              }
            : c,
        ),
      );
      return { previous };
    },
    onError: (err, _req, ctx) => {
      if (ctx?.previous) qc.setQueryData(['customer', 'consents'], ctx.previous);
      Alert.alert(
        i18n.t('error.generic'),
        err instanceof Error ? err.message : i18n.t('error.tryAgain'),
      );
    },
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: ['customer', 'consents'] });
    },
  });
}
