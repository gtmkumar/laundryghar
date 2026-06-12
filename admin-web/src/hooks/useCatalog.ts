import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo } from 'react'
import {
  getServiceCategories,
  getServices,
  getItems,
  getFabricTypes,
  getItemGroups,
  getPriceLists,
  getPriceListItems,
  getAdminCustomers,
  updateAdminCustomer,
  createServiceCategory,
  updateServiceCategory,
  deleteServiceCategory,
  createService,
  updateService,
  deleteService,
  createItem,
  updateItem,
  deleteItem,
  uploadItemImage,
  deleteItemImage,
  getItemImageBlob,
  createPriceList,
  updatePriceList,
  publishPriceList,
  deletePriceList,
  createPriceListItem,
  updatePriceListItem,
} from '@/api/catalog'
import type {
  PaginationParams,
  AdminCustomerListParams,
  AdminUpdateCustomerPayload,
  CreateServiceCategoryPayload,
  UpdateServiceCategoryPayload,
  CreateServicePayload,
  UpdateServicePayload,
  CreateItemPayload,
  UpdateItemPayload,
  CreatePriceListPayload,
  UpdatePriceListPayload,
  CreatePriceListItemPayload,
  UpdatePriceListItemPayload,
} from '@/types/api'

export const catalogKeys = {
  categories: (params?: object) => ['catalog', 'categories', params] as const,
  categoriesInfinite: (params?: object) => ['catalog', 'categories', 'infinite', params] as const,
  services: (params?: object) => ['catalog', 'services', params] as const,
  servicesInfinite: (params?: object) => ['catalog', 'services', 'infinite', params] as const,
  items: (params?: object) => ['catalog', 'items', params] as const,
  itemsInfinite: (params?: object) => ['catalog', 'items', 'infinite', params] as const,
  itemImage: (itemId: string) => ['catalog', 'item-image', itemId] as const,
  fabricTypes: (params?: object) => ['catalog', 'fabricTypes', params] as const,
  itemGroups: (params?: object) => ['catalog', 'itemGroups', params] as const,
  priceLists: (params?: object) => ['catalog', 'priceLists', params] as const,
  priceListsInfinite: (params?: object) => ['catalog', 'priceLists', 'infinite', params] as const,
  priceListItems: (priceListId: string) => ['catalog', 'priceListItems', priceListId] as const,
  adminCustomers: (params?: object) => ['catalog', 'adminCustomers', params] as const,
}

const CATALOG_PAGE_SIZE = 100

export function useServiceCategories(params: PaginationParams = {}) {
  return useQuery({
    queryKey: catalogKeys.categories(params),
    queryFn: () => getServiceCategories(params),
  })
}

export function useServiceCategoriesInfinite() {
  return useInfiniteQuery({
    queryKey: catalogKeys.categoriesInfinite(),
    queryFn: ({ pageParam }) =>
      getServiceCategories({ page: pageParam, pageSize: CATALOG_PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.hasNextPage ? allPages.length + 1 : undefined,
  })
}

export function useServices(params: PaginationParams & { categoryId?: string } = {}) {
  return useQuery({
    queryKey: catalogKeys.services(params),
    queryFn: () => getServices(params),
  })
}

export function useServicesInfinite() {
  return useInfiniteQuery({
    queryKey: catalogKeys.servicesInfinite(),
    queryFn: ({ pageParam }) =>
      getServices({ page: pageParam, pageSize: CATALOG_PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.hasNextPage ? allPages.length + 1 : undefined,
  })
}

export function useItems(params: PaginationParams & { itemGroupId?: string } = {}) {
  return useQuery({
    queryKey: catalogKeys.items(params),
    queryFn: () => getItems(params),
  })
}

export function useItemsInfinite() {
  return useInfiniteQuery({
    queryKey: catalogKeys.itemsInfinite(),
    queryFn: ({ pageParam }) => getItems({ page: pageParam, pageSize: CATALOG_PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.hasNextPage ? allPages.length + 1 : undefined,
  })
}

/** Flat fabric-type lookup (first 100) — feeds the price-list item editor. */
export function useFabricTypes() {
  return useQuery({
    queryKey: catalogKeys.fabricTypes(),
    queryFn: () => getFabricTypes(),
    staleTime: 5 * 60_000,
  })
}

/** Flat item-group lookup (first 100) — feeds item + price-list item editors. */
export function useItemGroups() {
  return useQuery({
    queryKey: catalogKeys.itemGroups(),
    queryFn: () => getItemGroups(),
    staleTime: 5 * 60_000,
  })
}

export function usePriceLists(params: PaginationParams = {}) {
  return useQuery({
    queryKey: catalogKeys.priceLists(params),
    queryFn: () => getPriceLists(params),
  })
}

export function usePriceListsInfinite() {
  return useInfiniteQuery({
    queryKey: catalogKeys.priceListsInfinite(),
    queryFn: ({ pageParam }) =>
      getPriceLists({ page: pageParam, pageSize: CATALOG_PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.hasNextPage ? allPages.length + 1 : undefined,
  })
}

/** Priced rows inside one price list. */
export function usePriceListItems(priceListId: string | null) {
  return useQuery({
    queryKey: catalogKeys.priceListItems(priceListId ?? ''),
    queryFn: () => getPriceListItems(priceListId as string),
    enabled: !!priceListId,
  })
}

// ── Catalog mutations ─────────────────────────────────────────────────────────

function invalidate(qc: ReturnType<typeof useQueryClient>, ...keys: readonly (readonly unknown[])[]) {
  for (const key of keys) void qc.invalidateQueries({ queryKey: key })
}

export function useCreateServiceCategory() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateServiceCategoryPayload) => createServiceCategory(payload),
    onSuccess: () => invalidate(qc, ['catalog', 'categories']),
  })
}

export function useUpdateServiceCategory() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdateServiceCategoryPayload }) =>
      updateServiceCategory(id, payload),
    onSuccess: () => invalidate(qc, ['catalog', 'categories']),
  })
}

export function useDeleteServiceCategory() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteServiceCategory(id),
    onSuccess: () => invalidate(qc, ['catalog', 'categories']),
  })
}

export function useCreateService() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateServicePayload) => createService(payload),
    onSuccess: () => invalidate(qc, ['catalog', 'services']),
  })
}

export function useUpdateService() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdateServicePayload }) =>
      updateService(id, payload),
    onSuccess: () => invalidate(qc, ['catalog', 'services']),
  })
}

export function useDeleteService() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteService(id),
    onSuccess: () => invalidate(qc, ['catalog', 'services']),
  })
}

export function useCreateItem() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateItemPayload) => createItem(payload),
    onSuccess: () => invalidate(qc, ['catalog', 'items']),
  })
}

export function useUpdateItem() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdateItemPayload }) =>
      updateItem(id, payload),
    onSuccess: () => invalidate(qc, ['catalog', 'items']),
  })
}

export function useDeleteItem() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteItem(id),
    onSuccess: () => invalidate(qc, ['catalog', 'items']),
  })
}

export function useUploadItemImage() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, file }: { id: string; file: File }) => uploadItemImage(id, file),
    onSuccess: (_, { id }) =>
      invalidate(qc, ['catalog', 'items'], catalogKeys.itemImage(id)),
  })
}

export function useDeleteItemImage() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteItemImage(id),
    onSuccess: (_, id) => invalidate(qc, ['catalog', 'items'], catalogKeys.itemImage(id)),
  })
}

/**
 * Object URL for an item's stored image, fetched through the authenticated
 * streaming endpoint (a plain <img src> can't carry the bearer token).
 * Returns undefined while loading or when the item has no stored image.
 */
export function useItemImageUrl(itemId: string | undefined, hasImage: boolean) {
  const { data: blob } = useQuery({
    queryKey: catalogKeys.itemImage(itemId ?? ''),
    queryFn: () => getItemImageBlob(itemId!),
    enabled: !!itemId && hasImage,
    staleTime: 5 * 60 * 1000,
  })

  const url = useMemo(() => (blob ? URL.createObjectURL(blob) : undefined), [blob])
  useEffect(() => {
    return () => {
      if (url) URL.revokeObjectURL(url)
    }
  }, [url])

  return url
}

// ── Pricing mutations ─────────────────────────────────────────────────────────

export function useCreatePriceList() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreatePriceListPayload) => createPriceList(payload),
    onSuccess: () => invalidate(qc, ['catalog', 'priceLists']),
  })
}

export function useUpdatePriceList() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdatePriceListPayload }) =>
      updatePriceList(id, payload),
    onSuccess: () => invalidate(qc, ['catalog', 'priceLists']),
  })
}

export function usePublishPriceList() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => publishPriceList(id),
    onSuccess: () => invalidate(qc, ['catalog', 'priceLists']),
  })
}

export function useDeletePriceList() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deletePriceList(id),
    onSuccess: () => invalidate(qc, ['catalog', 'priceLists']),
  })
}

export function useCreatePriceListItem(priceListId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreatePriceListItemPayload) => createPriceListItem(priceListId, payload),
    onSuccess: () => invalidate(qc, catalogKeys.priceListItems(priceListId)),
  })
}

export function useUpdatePriceListItem(priceListId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdatePriceListItemPayload }) =>
      updatePriceListItem(priceListId, id, payload),
    onSuccess: () => invalidate(qc, catalogKeys.priceListItems(priceListId)),
  })
}

// ── Admin Customers ───────────────────────────────────────────────────────────

export function useAdminCustomers(params: AdminCustomerListParams = {}, enabled = true) {
  return useQuery({
    queryKey: catalogKeys.adminCustomers(params),
    queryFn: () => getAdminCustomers(params),
    enabled,
    // Customer names change rarely — 5 min stale time is fine
    staleTime: 5 * 60_000,
  })
}

export function useUpdateAdminCustomer() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: AdminUpdateCustomerPayload }) =>
      updateAdminCustomer(id, payload),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['catalog', 'adminCustomers'] })
    },
  })
}

/**
 * Returns a stable Map<customerId, displayName> derived from the first page of
 * admin customers (pageSize 100). Falls back gracefully if the query is still
 * loading or returns no data. Used in the dashboard live-order feed.
 */
export function useCustomerNameMap(enabled = true): Map<string, string> {
  const { data } = useAdminCustomers({ pageSize: 100 }, enabled)

  return useMemo(() => {
    const map = new Map<string, string>()
    for (const c of data?.list ?? []) {
      // Prefer displayName, then firstName+lastName, then customerCode
      const name =
        c.displayName?.trim() ||
        [c.firstName, c.lastName].filter(Boolean).join(' ').trim() ||
        c.customerCode
      map.set(c.id, name)
    }
    return map
  }, [data])
}
